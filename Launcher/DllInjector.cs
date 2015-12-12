﻿using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Launcher.WindowsAPI;

namespace Launcher
{
    public enum DllInjectionResult
    {
        DllNotFound,
        GameProcessNotFound,
        InjectionFailed,
        Success
    }

    public sealed class DllInjector
    {
        static readonly IntPtr IntptrZero = (IntPtr)0;

        static DllInjector _instance;

        public static DllInjector GetInstance
        {
            get { return _instance ?? (_instance = new DllInjector()); }
        }

        DllInjector() { }

        public DllInjectionResult Inject(string sProcName, string sDllPath)
        {
            if (!File.Exists(sDllPath))
                return DllInjectionResult.DllNotFound;

            uint procId = 0;

            var procs = Process.GetProcesses();
            for (var i = 0; i < procs.Length; i++)
            {
                if (procs[i].ProcessName == sProcName)
                {
                    procId = (uint)procs[i].Id;
                    break;
                }
            }

            if (procId == 0)
                return DllInjectionResult.GameProcessNotFound;

            if (!this.BInject(procId, sDllPath))
                return DllInjectionResult.InjectionFailed;

            return DllInjectionResult.Success;
        }

        public bool BInject(uint pToBeInjected, string sDllPath)
        {
            var hndProc = Kernel32.OpenProcess((uint) (Kernel32.ProcessAccessFlags.CreateThread | Kernel32.ProcessAccessFlags.VirtualMemoryOperation
                                              | Kernel32.ProcessAccessFlags.VirtualMemoryRead | Kernel32.ProcessAccessFlags.VirtualMemoryWrite
                                              | Kernel32.ProcessAccessFlags.QueryInformation), 1, pToBeInjected);
            if (hndProc == IntptrZero)
                return false;

            var lpLlAddress = Kernel32.GetProcAddress(Kernel32.GetModuleHandle("kernel32.dll"), "LoadLibraryA");
            if (lpLlAddress == IntptrZero)
                return false;

            var lpAddress = Kernel32.VirtualAllocEx(hndProc, (IntPtr)null, (IntPtr)sDllPath.Length, 
                (int)(Kernel32.AllocationType.Commit | Kernel32.AllocationType.Reserve), (int)Kernel32.MemoryProtection.ExecuteReadWrite);
            if (lpAddress == IntptrZero)
                return false;

            var bytes = Encoding.ASCII.GetBytes(sDllPath);

            if (Kernel32.WriteProcessMemory(hndProc, lpAddress, bytes, (uint)bytes.Length, 0) == 0)
                return false;

            if (Kernel32.CreateRemoteThread(hndProc, (IntPtr)null, IntptrZero, lpLlAddress, lpAddress, 0, (IntPtr)null) == IntptrZero)
                return false;

            Kernel32.CloseHandle(hndProc);

            return true;
        }
    } 
}
