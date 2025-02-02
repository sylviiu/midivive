﻿using NAudio.Midi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using Mono.Options;

namespace MidiVive
{
    class Settings
    {
        public bool Debug = false;
        public string InputFile = null;
        public float Volume = 0.25F;
        public float Tolerance = 40F;
    }
    class Program
    {

        static void Main(string[] args)
        {
            bool showHelp = false;
            Settings settings = new Settings();

            OptionSet optionSet = new OptionSet() {
                { "i|input=", "name of the *.mid file",
                   v => settings.InputFile = v },
                { "v|volume=",
                   "playback volume (haptic intensity) from 0 to 1",
                    (float v) => settings.Volume = v },
                { "t|tolerance=",
                   "time in ms during which controller can start playing another note before the current one finishes playing",
                    (float v) => settings.Tolerance = v },
                { "d|debug",  "show debugging messages and play sound into speakers",
                   v => settings.Debug = v != null },
                { "h|help",  "show this message and exit",
                   v => showHelp = v != null },
                
            };

            List<string> extra;
            try
            {
                extra = optionSet.Parse(args);
            }
            catch (OptionException e)
            {
                Console.WriteLine("Error while parsing arguments: ");
                Console.WriteLine(e.Message);
                Console.WriteLine("Try starting with '--help' for more information.");
                return;
            }

            if (showHelp)
            {
                optionSet.WriteOptionDescriptions(Console.Out);
                return;
            }

            if(settings.InputFile == null)
            {
                Console.WriteLine("Input file not set! Use -i <*.mid>");
                return;
            }
            
            Run(settings);
        }


        static void Run(Settings settings)
        {
            if (!OpenVRHelper.InitHMD() && !settings.Debug)
            {
                return;
            }

            Console.WriteLine("Found {0} devices", OpenVRHelper.DeviceCount());
            
            string inputFile = settings.InputFile;
            if (!File.Exists(inputFile))
            {
                Console.WriteLine("Input file {0} doesn't exist!", inputFile);
                return;
            }
            
            MidiFile midi = new MidiFile(inputFile, false);

            Console.WriteLine("Found {0} devices", OpenVRHelper.DeviceCount());

            Player[] players = new Player[OpenVRHelper.DeviceCount()];
            for (int i = 0; i < players.Count(); i++)
            {
                players[i] = new Player(OpenVRHelper.DeviceHandles[i], settings.Tolerance, settings.Debug);
            }
            
            //these will bet set correctly before any notes are played
            double bpm = 120;
            double ticksPerQuarter = 1;
            double msPerQuarter = 1;

            int divison = midi.DeltaTicksPerQuarterNote;
            
            int[] trackEventIndex = new int[midi.Events.Count()];
            long currentTick = 0;
            Stopwatch sw = Stopwatch.StartNew();
            int tracksStopped = 0;
            //based on https://github.com/ipatix/serialmidi/blob/master/serialmidi/Program.cs
            //and https://gitlab.com/Pilatomic/SteamControllerSinger/blob/master/main.cpp
            while (true)
            {
                for (int trackNum = 0; trackNum < midi.Events.Count(); trackNum++)
                {
                    var track = midi.Events[trackNum];
                    if (trackEventIndex[trackNum] >= track.Count())
                        continue;

                    while (currentTick >= track[trackEventIndex[trackNum]].AbsoluteTime)
                    {
                        var ev = track[trackEventIndex[trackNum]];
                        if (ev.CommandCode == MidiCommandCode.NoteOn)
                        {
                            NoteOnEvent note = (NoteOnEvent)ev;
                            if (note.OffEvent == null)
                            {
                                Console.WriteLine("Note {0} doesn't have an off event, skipping.", note.NoteName);
                            }
                            else
                            {
                                //calculate note duration 
                                double duration = note.NoteLength * msPerQuarter;

                                //calculate frequency from the note number
                                //https://pages.mtu.edu/~suits/NoteFreqCalcs.html
                                double frequency = (440F * Math.Pow(2, (note.NoteNumber - 69) / 12F));

                                for (int i = 0; i < players.Count(); i++)
                                {
                                    if (players[i].IsBusy())
                                    {
                                        Console.WriteLine("{0}/{1} busy", i, players.Count() - 1);

                                        if (i == players.Count() - 1)
                                        {
                                            Console.WriteLine("{0}/{1} busy. Skipping note.", i, players.Count() - 1);
                                        }

                                        continue;
                                    }
                                    players[i].Play(duration, frequency, settings.Volume);
                                    break;
                                }
                            }

                        }
                        if (ev.CommandCode == MidiCommandCode.MetaEvent)
                        {
                            MetaEvent meta = (MetaEvent)ev;
                            if (meta.MetaEventType == MetaEventType.SetTempo)
                            {
                                TempoEvent tempoEvent = (TempoEvent)meta;
                                bpm = tempoEvent.Tempo;
                                if (settings.Debug)
                                {
                                    Console.WriteLine("New tempo: " + bpm + " BPM");
                                }
                                //https://stackoverflow.com/questions/2038313/converting-midi-ticks-to-actual-playback-seconds
                                ticksPerQuarter = (60 * Stopwatch.Frequency) / (bpm * divison);
                                msPerQuarter = (60000F / (bpm * divison));
                            }
                        }
                        trackEventIndex[trackNum]++;
                        if (trackEventIndex[trackNum] == track.Count)
                        {
                            tracksStopped++;
                            if (settings.Debug)
                            {
                                Console.WriteLine("Stopped track {0}/{1}", tracksStopped, midi.Events.Count());
                            }
                            break;
                        }

                    }
                }
                if (tracksStopped == midi.Events.Count())
                    break;

                currentTick++;

                //just skip cycles for now
                while (sw.ElapsedTicks < ticksPerQuarter)
                {
                    ;
                }
                sw.Restart();
            }
            sw.Stop();

            Console.WriteLine("Stopped");
            OpenVRHelper.Shutdown();
        }
        ///<summary>
        ///A debug class that asynchronously plays tones to the sound device
        ///</summary>
        class Beep
        {
            private readonly float duration;
            private readonly float volume;
            private readonly float frequency;

            public Beep(float frequency, float duration, float volume)
            {
                this.duration = duration;
                this.volume = volume;
                this.frequency = frequency;
            }

            public void Play()
            {
                new Thread(Play0).Start();
            }

            private void Play0()
            {
                SignalGenerator sineWaveProvider = new SignalGenerator
                {
                    Frequency = frequency
                };
                WaveOut waveOut = new WaveOut();
                waveOut.Init(sineWaveProvider);
                waveOut.Volume = volume;
                waveOut.Play();
                Thread.Sleep((int)duration);
                waveOut.Stop();
            }
        }
        
        class Player
        {
            private long started;
            private double duration;
            private readonly int controller;
            private readonly float tolerance;
            private readonly bool debugMode;

            public Player(OpenVRHelper.Device controller, float tolerance, bool debugMode)
            {
                this.controller = Convert.ToInt32(controller.index);
                this.tolerance = tolerance;
                this.debugMode = debugMode;
                Console.WriteLine("Player: {0} (at {1})", this.controller, controller.path);
            }

            private long GetUnixTime()
            {
                return ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds();
            }

            public bool IsBusy()
            {
                return GetUnixTime() - started < duration - tolerance;
            }

            public void Play(double durationMS, double frequency, float volume)
            {
                started = GetUnixTime();
                duration = durationMS;
                if (debugMode)
                {
                    new Beep((int)frequency, (int)duration, volume).Play();
                }
                else
                {
                    OpenVRHelper.Device device = OpenVRHelper.DeviceHandles[controller];
                    Console.WriteLine("[{0}] Playing note for controller at ({1:0.##} Hz) for {2:0.##} ", device.index, frequency, durationMS);
                    OpenVRHelper.PlayNote(OpenVRHelper.DeviceHandles[controller], (float)durationMS / 1000F, (float)frequency, volume);
                }
            }
        }
        
    }

    
}
