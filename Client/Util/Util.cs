﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GTA.Math;
using GTA;
using GTA.Native;
using System.IO;
using System.Xml.Serialization;
using System.Runtime.InteropServices;

namespace RageCoop.Client
{
    internal static class Util
    {
        #region -- POINTER --
        private static int _steeringAngleOffset { get; set; }

        public static unsafe void NativeMemory()
        {
            IntPtr address;

            address = Game.FindPattern("\x74\x0A\xF3\x0F\x11\xB3\x1C\x09\x00\x00\xEB\x25", "xxxxxx????xx");
            if (address != IntPtr.Zero)
            {
                _steeringAngleOffset = *(int*)(address + 6) + 8;
            }

            // breaks some stuff.
            /*
            address = Game.FindPattern("\x32\xc0\xf3\x0f\x11\x09", "xxxxxx"); // Weapon / Radio slowdown
            if (address != IntPtr.Zero)
            {
                for (int i = 0; i < 6; i++)
                {
                    *(byte*)(address + i).ToPointer() = 0x90;
                }
            }
            */
        }

        public static unsafe void CustomSteeringAngle(this Vehicle veh, float value)
        {
            IntPtr address = new IntPtr((long)veh.MemoryAddress);
            if (address == IntPtr.Zero || _steeringAngleOffset == 0)
            {
                return;
            }

            *(float*)(address + _steeringAngleOffset).ToPointer() = value;
        }
        #endregion
        #region MATH
        public static Vector3 LinearVectorLerp(Vector3 start, Vector3 end, ulong currentTime, int duration)
        {
            return new Vector3()
            {
                X = LinearFloatLerp(start.X, end.X, currentTime, duration),
                Y = LinearFloatLerp(start.Y, end.Y, currentTime, duration),
                Z = LinearFloatLerp(start.Z, end.Z, currentTime, duration),
            };
        }

        public static float LinearFloatLerp(float start, float end, ulong currentTime, int duration)
        {
            return (end - start) * currentTime / duration + start;
        }

        public static float Lerp(float from, float to, float fAlpha)
        {
            return (from * (1.0f - fAlpha)) + (to * fAlpha); //from + (to - from) * fAlpha
        }

        public static Vector3 RotationToDirection(Vector3 rotation)
        {
            double z = MathExtensions.DegToRad(rotation.Z);
            double x = MathExtensions.DegToRad(rotation.X);
            double num = Math.Abs(Math.Cos(x));

            return new Vector3
            {
                X = (float)(-Math.Sin(z) * num),
                Y = (float)(Math.Cos(z) * num),
                Z = (float)Math.Sin(x)
            };
        }


        #endregion
        public static Settings ReadSettings()
        {
            XmlSerializer ser = new XmlSerializer(typeof(Settings));

            string path = Directory.GetCurrentDirectory() + "\\Scripts\\RageCoop\\RageCoop.Client.Settings.xml";
            Settings settings = null;

            if (File.Exists(path))
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    settings = (RageCoop.Client.Settings)ser.Deserialize(stream);
                }

                using (FileStream stream = new FileStream(path, FileMode.Truncate, FileAccess.ReadWrite))
                {
                    ser.Serialize(stream, settings);
                }
            }
            else
            {
                using (FileStream stream = File.OpenWrite(path))
                {
                    ser.Serialize(stream, settings = new Settings());
                }
            }

            return settings;
        }
        public static void SaveSettings()
        {
            try
            {
                string path = Directory.GetCurrentDirectory() + "\\Scripts\\RageCoop\\RageCoop.Client.Settings.xml";

                using (FileStream stream = new FileStream(path, File.Exists(path) ? FileMode.Truncate : FileMode.Create, FileAccess.ReadWrite))
                {
                    XmlSerializer ser = new XmlSerializer(typeof(Settings));
                    ser.Serialize(stream, Main.Settings);
                }
            }
            catch (Exception ex)
            {
                GTA.UI.Notification.Show("Error saving player settings: " + ex.Message);
            }
        }

        [DllImport("kernel32.dll")]
        public static extern ulong GetTickCount64();
        public static Vector3 PredictPosition(this Entity e, bool applyDefault = true)
        {
            return e.Position+e.Velocity*((applyDefault ? SyncParameters.PositioinPredictionDefault : 0)+Networking.Latency);
        }

        public static Model ModelRequest(this int hash)
        {
            Model model = new Model(hash);

            if (!model.IsValid)
            {
                //GTA.UI.Notification.Show("~y~Not valid!");
                return null;
            }

            if (!model.IsLoaded)
            {
                return model.Request(1000) ? model : null;
            }

            return model;
        }
        public static void SetOnFire(this Entity e, bool toggle)
        {
            if (toggle)
            {
                Function.Call(Hash.START_ENTITY_FIRE, e.Handle);
            }
            else
            {
                Function.Call(Hash.STOP_ENTITY_FIRE, e.Handle);
            }
        }

        public static SyncedPed GetSyncEntity(this Ped p)
        {
            if (p == null) { return null; }
            var c = EntityPool.GetPedByHandle(p.Handle);
            if (c==null) { EntityPool.Add(c=new SyncedPed(p)); }
            return c;
        }

        public static SyncedVehicle GetSyncEntity(this Vehicle veh)
        {
            if (veh == null) { return null; }
            var v = EntityPool.GetVehicleByHandle(veh.Handle);
            if (v==null) { EntityPool.Add(v=new SyncedVehicle(veh)); }
            return v;
        }

        public static byte GetPlayerRadioIndex()
        {
            return (byte)Function.Call<int>(Hash.GET_PLAYER_RADIO_STATION_INDEX);
        }
        public static void SetPlayerRadioIndex(int index)
        {
            Function.Call(Hash.SET_RADIO_TO_STATION_INDEX, index);
        }

    }
}
