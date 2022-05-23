﻿using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Diagnostics;
using RageCoop.Client.Menus;
using RageCoop.Core;
using GTA;
using GTA.Native;
using GTA.Math;

namespace RageCoop.Client
{
    /// <summary>
    /// Don't use it!
    /// </summary>
    internal class Main : Script
    {

        private bool _gameLoaded = false;
        private static bool _isGoingToCar = false;

        public static readonly string CurrentVersion = "V0_1";

        public static int LocalPlayerID=0;
        public static bool DisableTraffic = true;
        public static bool NPCsAllowed = false;
        internal static RelationshipGroup SyncedPedsGroup;

        public static new Settings Settings = null;

#if !NON_INTERACTIVE
        public static RageCoopMenu MainMenu = null;
#endif
        public static Chat MainChat = null;
        public static Stopwatch Counter = new Stopwatch();

        public static ulong Ticked = 0;
        public static Loggger Logger=new Loggger("Scripts\\RageCoop\\RageCoop.Client.log");
        
        private static List<Func<bool>> QueuedActions = new List<Func<bool>>();

        /// <summary>
        /// Don't use it!
        /// </summary>
        public Main()
        {

            // Required for some synchronization!
            /*if (Game.Version < GameVersion.v1_0_1290_1_Steam)
            {
                Tick += (object sender, EventArgs e) =>
                {
                    if (Game.IsLoading)
                    {
                        return;
                    }
                    
                    if (!_gameLoaded)
                    {
                        GTA.UI.Notification.Show("~r~Please update your GTA5 to v1.0.1290 or newer!", true);
                        _gameLoaded = true;
                    }
                };
                return;
            }*/
            SyncedPedsGroup=World.AddRelationshipGroup("SYNCPED");
            Game.Player.Character.RelationshipGroup.SetRelationshipBetweenGroups(SyncedPedsGroup, Relationship.Neutral, true);
            Settings = Util.ReadSettings();
            Networking.Start();
#if !NON_INTERACTIVE
            MainMenu = new RageCoopMenu();
#endif
            MainChat = new Chat();
#if DEBUG
            Logger.LogLevel =0;
#else
            Logger.LogLevel=Settings.LogLevel;
#endif

            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += (object sender, EventArgs e) => CleanUp();
            
            Util.NativeMemory();
            Counter.Restart();
        }
        
#if DEBUG
        private ulong _lastDebugData;
        private int _debugBytesSend;
        private int _debugBytesReceived;
#endif
        private void OnTick(object sender, EventArgs e)
        {
            if (Game.IsLoading)
            {
                return;
            }
            else if (!_gameLoaded && (_gameLoaded = true))
            {
#if !NON_INTERACTIVE
                GTA.UI.Notification.Show(GTA.UI.NotificationIcon.AllPlayersConf, "RAGECOOP","Welcome!", $"Press ~g~{Main.Settings.MenuKey}~s~ to open the menu.");
#endif
            }

#if !NON_INTERACTIVE
            MainMenu.MenuPool.Process();
#endif
            

            if (_isGoingToCar && Game.Player.Character.IsInVehicle())
            {
                _isGoingToCar = false;
            }
            DoQueuedActions();
            if (!Networking.IsOnServer())
            {
                return;
            }
            if (Game.TimeScale!=1)
            {
                Game.TimeScale=1;
            }
            try
            {
                Networking.Tick();
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
            
            if (!DownloadManager.DownloadComplete)
            {
                DownloadManager.RenderProgress();
            }

            MapLoader.LoadAll();

#if DEBUG
            if (Networking.ShowNetworkInfo)
            {
                ulong time = Util.GetTickCount64();
                if (time - _lastDebugData > 1000)
                {
                    _lastDebugData = time;

                    _debugBytesReceived = Networking.BytesReceived;
                    Networking.BytesReceived = 0;
                    _debugBytesSend = Networking.BytesSend;
                    Networking.BytesSend = 0;
                }

                new LemonUI.Elements.ScaledText(new PointF(Screen.PrimaryScreen.Bounds.Width / 2, 0), $"L: {Networking.Latency * 1000:N0}ms", 0.5f) { Alignment = GTA.UI.Alignment.Center }.Draw();
                new LemonUI.Elements.ScaledText(new PointF(Screen.PrimaryScreen.Bounds.Width / 2, 30), $"R: {Lidgren.Network.NetUtility.ToHumanReadable(_debugBytesReceived)}/s", 0.5f) { Alignment = GTA.UI.Alignment.Center }.Draw();
                new LemonUI.Elements.ScaledText(new PointF(Screen.PrimaryScreen.Bounds.Width / 2, 60), $"S: {Lidgren.Network.NetUtility.ToHumanReadable(_debugBytesSend)}/s", 0.5f) { Alignment = GTA.UI.Alignment.Center }.Draw();
            }
#endif



            MainChat.Tick();
            PlayerList.Tick();




            Ticked++;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (MainChat.Focused)
            {
                MainChat.OnKeyDown(e.KeyCode);
                return;
            }
            if (Game.IsControlPressed(GTA.Control.FrontendPause))
            {
                Function.Call(Hash.ACTIVATE_FRONTEND_MENU, Function.Call<int>(Hash.GET_HASH_KEY, "FE_MENU_VERSION_SP_PAUSE"), false, 0);
                return;
            }
            if(e.KeyCode == Settings.MenuKey)
            {
                if (MainMenu.MenuPool.AreAnyVisible)
                {
                    MainMenu.MainMenu.Visible = false;
                    MainMenu.SubSettings.MainMenu.Visible = false;
                }
                else
                {
                    MainMenu.MainMenu.Visible = true;
                }
            }
            else if (Game.IsControlJustPressed(GTA.Control.MultiplayerInfo))
            {
                if (Networking.IsOnServer())
                {
                    ulong currentTimestamp = Util.GetTickCount64();
                    PlayerList.Pressed = (currentTimestamp - PlayerList.Pressed) < 5000 ? (currentTimestamp - 6000) : currentTimestamp;
                }
            }
            else if (Game.IsControlJustPressed(GTA.Control.MpTextChatAll))
            {
                if (Networking.IsOnServer())
                {
                    MainChat.Focused = true;
                }
            }
            else if (e.KeyCode==Settings.PassengerKey)
            {
                var P = Game.Player.Character;
                
                if (!P.IsInVehicle())
                {
                    if (P.IsTaskActive(TaskType.CTaskEnterVehicle))
                    {
                        P.Task.ClearAll();
                    }
                    else
                    {
                        var V = World.GetClosestVehicle(P.Position, 50);

                        if (V!=null)
                        {
                            var seat = Util.GetNearestSeat(P, V);
                            P.Task.EnterVehicle(V, seat);
                        }
                    }
                }
            }
        }
        public static void CleanUp()
        {
            MainChat.Clear();
            EntityPool.Cleanup();
            PlayerList.Cleanup();
            Main.LocalPlayerID=default;

        }

        public static readonly Dictionary<ulong, byte> CheckNativeHash = new Dictionary<ulong, byte>()
        {
            { 0xD49F9B0955C367DE, 1 }, // Entities
            { 0xEF29A16337FACADB, 1 }, //
            { 0xB4AC7D0CF06BFE8F, 1 }, //
            { 0x9B62392B474F44A0, 1 }, //
            { 0x7DD959874C1FD534, 1 }, //
            { 0xAF35D0D2583051B0, 2 }, // Vehicles
            { 0x63C6CCA8E68AE8C8, 2 }, //
            { 0x509D5878EB39E842, 3 }, // Props
            { 0x9A294B2138ABB884, 3 }, //
            { 0x46818D79B1F7499A, 4 }, // Blips
            { 0x5CDE92C702A8FCE7, 4 }, //
            { 0xBE339365C863BD36, 4 }, //
            { 0x5A039BB0BCA604B6, 4 }, //
            { 0x0134F0835AB6BFCB, 5 }  // Checkpoints
        };
        public static Dictionary<int, byte> ServerItems = new Dictionary<int, byte>();
        public static void CleanUpWorld()
        {
            if (ServerItems.Count == 0)
            {
                return;
            }

            lock (ServerItems)
            {
                foreach (KeyValuePair<int, byte> item in ServerItems)
                {
                    try
                    {
                        switch (item.Value)
                        {
                            case 1:
                                World.GetAllEntities().FirstOrDefault(x => x.Handle == item.Key)?.Delete();
                                break;
                            case 2:
                                World.GetAllVehicles().FirstOrDefault(x => x.Handle == item.Key)?.Delete();
                                break;
                            case 3:
                                World.GetAllProps().FirstOrDefault(x => x.Handle == item.Key)?.Delete();
                                break;
                            case 4:
                                Blip blip = new Blip(item.Key);
                                if (blip.Exists())
                                {
                                    blip.Delete();
                                }
                                break;
                            case 5:
                                Checkpoint checkpoint = new Checkpoint(item.Key);
                                if (checkpoint.Exists())
                                {
                                    checkpoint.Delete();
                                }
                                break;
                        }
                    }
                    catch
                    {
                        GTA.UI.Notification.Show("~r~~h~CleanUpWorld() Error");
                        Logger.Error($"CleanUpWorld(): ~r~Item {item.Value} cannot be deleted!");
                    }
                }

                ServerItems.Clear();
            }
        }

        private static void DoQueuedActions()
        {
            lock (QueuedActions)
            {
                foreach (var action in QueuedActions.ToArray())
                {
                    try
                    {
                        if (action())
                        {
                            QueuedActions.Remove(action);
                        }
                    }
                    catch(Exception ex)
                    {
                        GTA.UI.Screen.ShowSubtitle(ex.ToString());
                        QueuedActions.Remove(action);
                    }
                }
            }
        }

        /// <summary>
        /// Queue an action  to be executed on next tick, allowing you to call scripting API from another thread.
        /// </summary>
        /// <param name="a"> The action to be executed, must return a bool indicating whether the action cane be removed after execution.</param>
        public static void QueueAction(Func<bool> a)
        {
            lock (QueuedActions)
            {
                QueuedActions.Add(a);
            }
        }
        public static void QueueAction(Action a)
        {
            lock (QueuedActions)
            {
                QueuedActions.Add(() => { a(); return true; }) ;
            }
        }
        /// <summary>
        /// Clears all queued actions
        /// </summary>
        public static void ClearQueuedActions()
        {
            lock (QueuedActions) { QueuedActions.Clear(); }
        }

        public static string DumpCharacters()
        {
            string s = "Characters:";
            lock (EntityPool.PedsLock)
            {
                foreach (int id in EntityPool.GetPedIDs())
                {
                    var c = EntityPool.GetPedByID(id);
                    s+=$"\r\nID:{c.ID} Owner:{c.OwnerID} LastUpdated:{c.LastUpdated} LastSynced:{c.LastSynced} LastStateSynced:{c.LastStateSynced}";
                    // s+=$"\r\n{c.IsAiming} {c.IsJumping} {c.IsOnFire} {c.IsOnLadder} {c.IsRagdoll} {c.IsReloading} {c.IsShooting} {c.Speed}";
                }
            }
            Logger.Trace(s);
            return s;
        }
        public static string DumpPlayers()
        {
            string s = "Players:";
            foreach (PlayerData p in PlayerList.Players)
            {
                
                s+=$"\r\nID:{p.PedID} Username:{p.Username}";
            }
            Logger.Trace(s);
            return s;
        }

    }
}
