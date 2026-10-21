// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.
// Standalone lightweight Windows Uninstaller Launcher (uninstall.exe) for Facebook One Client.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Facebook One Client Uninstaller")]
[assembly: AssemblyDescription("Uninstaller for Facebook One Client & Companion MCP Server")]
[assembly: AssemblyCompany("PPN Corporation")]
[assembly: AssemblyProduct("Facebook One Client")]
[assembly: AssemblyCopyright("Copyright (c) 2026 PPN Corporation")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: ComVisible(false)]

namespace Facebook.Net.Uninstaller
{
    internal static class Program
    {
        private const string AppRegistryKeyPath = @"Software\PPNCorporation\FacebookOneClient";
        private const string UninstallRegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private const string ProductDisplayName = "Facebook One Client";

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool quiet = false;
            bool purgeData = false;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i] ?? string.Empty;
                    if (string.Equals(arg, "/quiet", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(arg, "--quiet", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(arg, "-q", StringComparison.OrdinalIgnoreCase))
                    {
                        quiet = true;
                    }
                    else if (string.Equals(arg, "/purge", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(arg, "--purge-data", StringComparison.OrdinalIgnoreCase))
                    {
                        purgeData = true;
                    }
                }
            }

            string productCode = ResolveInstalledProductCode();
            if (string.IsNullOrWhiteSpace(productCode))
            {
                if (!quiet)
                {
                    MessageBox.Show(
                        "Không tìm thấy thông tin cài đặt MSI của Facebook One Client trên hệ thống.\n" +
                        "Facebook One Client MSI installation was not found on this machine.",
                        "Facebook One Client — Uninstall",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return 1;
            }

            StopRunningProcesses("FacebookOneClient");
            StopRunningProcesses("Facebook.Net.Mcp");

            string msiArgs = quiet
                ? "/x " + productCode + " /qn /norestart"
                : "/x " + productCode;

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "msiexec.exe",
                    Arguments = msiArgs,
                    UseShellExecute = true
                };

                using (Process proc = Process.Start(psi))
                {
                    if (proc != null)
                    {
                        proc.WaitForExit();
                        if (proc.ExitCode != 0)
                        {
                            return proc.ExitCode;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!quiet)
                {
                    MessageBox.Show(
                        "Không thể khởi chạy trình gỡ cài đặt Windows Installer:\n" + ex.Message,
                        "Facebook One Client — Uninstall",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                return 2;
            }

            // If uninstallation succeeded, optionally offer to remove %LocalAppData%\Facebook.Net
            string localDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Facebook.Net");

            if (Directory.Exists(localDataDir))
            {
                bool shouldDeleteData = purgeData;
                if (!quiet && !purgeData)
                {
                    DialogResult answer = MessageBox.Show(
                        "Đã gỡ cài đặt Facebook One Client thành công.\n\n" +
                        "Bạn có muốn xóa toàn bộ dữ liệu tài khoản, hồ sơ trình duyệt và cơ sở dữ liệu cục bộ tại:\n" +
                        localDataDir + " không?\n\n" +
                        "• Chọn [Yes] để xóa sạch hoàn toàn dữ liệu.\n" +
                        "• Chọn [No] nếu bạn muốn giữ lại dữ liệu cho lần cài đặt sau.",
                        "Facebook One Client — Dọn dẹp dữ liệu",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2);

                    shouldDeleteData = answer == DialogResult.Yes;
                }

                if (shouldDeleteData)
                {
                    try
                    {
                        Directory.Delete(localDataDir, true);
                    }
                    catch
                    {
                        // Ignore locked files inside profile folder if an external browser process is still open
                    }
                }
            }

            return 0;
        }

        private static string ResolveInstalledProductCode()
        {
            try
            {
                using (RegistryKey hklm64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (RegistryKey appKey = hklm64.OpenSubKey(AppRegistryKeyPath, false))
                    {
                        if (appKey != null)
                        {
                            string code = appKey.GetValue("ProductCode") as string;
                            if (!string.IsNullOrWhiteSpace(code))
                            {
                                return code.Trim();
                            }
                        }
                    }

                    using (RegistryKey uninstallRoot = hklm64.OpenSubKey(UninstallRegistryKeyPath, false))
                    {
                        if (uninstallRoot != null)
                        {
                            string[] subKeyNames = uninstallRoot.GetSubKeyNames();
                            for (int i = 0; i < subKeyNames.Length; i++)
                            {
                                string subName = subKeyNames[i];
                                if (!subName.StartsWith("{", StringComparison.Ordinal) ||
                                    !subName.EndsWith("}", StringComparison.Ordinal))
                                {
                                    continue;
                                }

                                using (RegistryKey subKey = uninstallRoot.OpenSubKey(subName, false))
                                {
                                    if (subKey == null)
                                    {
                                        continue;
                                    }

                                    string displayName = subKey.GetValue("DisplayName") as string;
                                    if (string.Equals(displayName, ProductDisplayName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        return subName;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore registry access exceptions
            }

            return string.Empty;
        }

        private static void StopRunningProcesses(string processName)
        {
            try
            {
                Process[] running = Process.GetProcessesByName(processName);
                for (int i = 0; i < running.Length; i++)
                {
                    try
                    {
                        if (running[i].CloseMainWindow())
                        {
                            if (running[i].WaitForExit(1500))
                            {
                                continue;
                            }
                        }

                        running[i].Kill();
                        running[i].WaitForExit(1500);
                    }
                    catch
                    {
                        // Ignore if process already exited
                    }
                    finally
                    {
                        running[i].Dispose();
                    }
                }
            }
            catch
            {
                // Ignore process enumeration errors
            }
        }
    }
}
