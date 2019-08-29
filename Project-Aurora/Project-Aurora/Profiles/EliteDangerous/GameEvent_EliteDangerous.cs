﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;
using System.Xml;
using Aurora.Profiles.EliteDangerous.GSI;
using Aurora.Profiles.EliteDangerous.GSI.Nodes;
using Aurora.Profiles.EliteDangerous.Helpers;
using Aurora.Profiles.EliteDangerous.Journal;
using Newtonsoft.Json;

namespace Aurora.Profiles.EliteDangerous
{
    public class GameEvent_EliteDangerous : GameEvent_Generic
    {
        private static readonly string journalFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Saved Games",
            "Frontier Developments",
            "Elite Dangerous"
        );

        private static readonly string statusFile = Path.Combine(journalFolder, "Status.json");

        private static readonly string bindingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Frontier Developments",
            "Elite Dangerous",
            "Options",
            "Bindings"
        );

        private static readonly string bindingsPresetFile = Path.Combine(bindingsFolder, "StartPreset.start");
        readonly DelayedMethodCaller delayedFileRead = new DelayedMethodCaller(1);
        private FileSystemWatcher bindWatcher = null;

        private string currentBindFile = null;
        private FileWatcher statusFileWatcher;

        public GameEvent_EliteDangerous() : base()
        {
            statusFileWatcher = new FileWatcher(statusFile, FileWatcher.ReadMode.FULL, StatusReadCallback);
        }
        
        public void StatusReadCallback(int lineNumber, string lineValue)
        {
            if (string.IsNullOrEmpty(lineValue)) return;

            Status newStatus = JsonConvert.DeserializeObject<Status>(lineValue);

            @UpdateStatus(newStatus);
        }
        
        public void UpdateStatus(Status newStatus)
        {
            Status status = (_game_state as GameState_EliteDangerous).Status;

            status.timestamp = newStatus.timestamp;
            status.@event = newStatus.@event;
            status.Flags = newStatus.Flags;
            status.Pips = newStatus.Pips;
            status.FireGroup = newStatus.FireGroup;
            status.GuiFocus = newStatus.GuiFocus;
            status.Fuel = newStatus.Fuel;
            status.Cargo = newStatus.Cargo;
        }
        
        
        public void JournalReadCallback(int lineNumber, string lineValue)
        {
            try
            {
                JsonSerializerSettings settings = new JsonSerializerSettings();
                JournalEvent newEvent = JsonConvert.DeserializeObject<JournalEvent>(lineValue, settings);
                if(newEvent != null)
                {
                    Global.logger.Error(newEvent + " : " + newEvent.@event);
                }
            }
            catch (JsonSerializationException e)
            {
                
            }
        }

        private void ReadAllJournalFiles()
        {
            foreach (string logFile in Directory.GetFiles(journalFolder, "*.log")
                .OrderBy(p => new FileInfo(p).CreationTime))
            {
                FileWatcher.ReadFileLines(logFile, JournalReadCallback);
            }
        }

        public void WatchBindFiles()
        {
            StopWatchingBindFiles();
            if (Directory.Exists(bindingsFolder))
            {
                bindWatcher = new FileSystemWatcher()
                {
                    Path = bindingsFolder,
                    NotifyFilter = NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
                bindWatcher.Changed += OnBindsFileChanged;
            }
        }

        public void StopWatchingBindFiles()
        {
            if (bindWatcher != null)
            {
                bindWatcher.Dispose();
                bindWatcher = null;
            }
        }

        private void OnBindsFileChanged(object sender, FileSystemEventArgs e)
        {
            /*
             * This event can fire multiple times in a row. We need to make sure to read the file only after
             * the last event is fired to avoid running into a locked file
             */
            delayedFileRead.CallMethod(() => ReadBindFiles());
        }

        private void ReadBindFiles()
        {
            if (File.Exists(bindingsPresetFile))
            {
                string currentBindPrefix = File.ReadAllText(bindingsPresetFile).Trim();
                foreach (string bindFile in Directory.GetFiles(bindingsFolder, currentBindPrefix + ".*.binds"))
                {
                    currentBindFile = bindFile;
                }

                if (currentBindFile != null)
                {
                    ParseBindFile();
                }
            }
        }

        private void ParseBindFile()
        {
            //TODO: Parse configuration XML
            Global.logger.Error("Current bind file: " + currentBindFile);

            HashSet<string> modifierKeys = new HashSet<string>();

            Dictionary<string, Bind> commandToBind = new Dictionary<string, Bind>();
            Dictionary<Bind, string> bindToCommand = new Dictionary<Bind, string>();

            XmlDocument doc = new XmlDocument();
            try
            {
                doc.Load(currentBindFile);
                XmlNodeList commands = doc.DocumentElement.ChildNodes;
                foreach (XmlNode command in commands)
                {
//                    if(command.HasChildNodes && command.SelectSingleNode("Deadzone") == null)
//                        Global.logger.Info("public const string " + command.Name + " = \"" + command.Name + "\";");
                    Bind bind = new Bind(command.Name);
                    foreach (XmlNode xmlMapping in command.ChildNodes)
                    {
                        if (xmlMapping.Name != "Primary" && xmlMapping.Name != "Secondary") continue;

                        Bind.Mapping mapping = new Bind.Mapping();

                        if (xmlMapping.Attributes["Device"].Value == "Keyboard")
                        {
                            mapping.SetKey(xmlMapping.Attributes["Key"].Value);
                        }

                        foreach (XmlNode property in xmlMapping.ChildNodes)
                        {
                            if (property.Name != "Modifier") continue;

                            if (property.Attributes["Device"].Value == "Keyboard" &&
                                !string.IsNullOrEmpty(property.Attributes["Key"].Value))
                            {
                                modifierKeys.Add(property.Attributes["Key"].Value);
                                mapping.AddModifier(property.Attributes["Key"].Value);
                            }
                        }

                        if (mapping.HasKey())
                        {
                            bind.AddMapping(mapping);
                        }
                    }

                    if (bind.mappings.Any())
                    {
                        commandToBind.Add(command.Name, bind);
                        bindToCommand.Add(bind, command.Name);
                    }
                }
            }
            catch (System.IO.FileNotFoundException)
            {
                Global.logger.Error("Error loading binds file: " + currentBindFile);
            }

            GSI.Nodes.Controls controls = (_game_state as GameState_EliteDangerous).Controls;
            controls.modifierKeys = modifierKeys;
            controls.commandToBind = commandToBind;
            controls.bindToCommand = bindToCommand;
        }

        public override void OnStart()
        {
            ReadBindFiles();
            WatchBindFiles();
            statusFileWatcher.Start();
            ReadAllJournalFiles();
            //TODO: Enable Journal API reading
        }

        public override void OnStop()
        {
            StopWatchingBindFiles();
            statusFileWatcher.Stop();
            //TODO: Disable Journal API reading
        }
    }
}