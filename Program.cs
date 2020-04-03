using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using PCSC;
using PCSC.Iso7816;
using PCSC.Monitoring;
using WindowsInput;
using WindowsInput.Native;

namespace NFCKbd
{
    class Program
    {
        static void Main(string[] args)
        {
            if (Process.GetProcesses().Count(
                p => p.ProcessName.ToLower() ==
                Path.GetFileNameWithoutExtension(AppDomain.CurrentDomain.SetupInformation.ApplicationName.ToLower())
                ) > 1)
            {
                Console.WriteLine("Only a single instance can run in a time");
                return;
            }

            var monitorFactory = MonitorFactory.Instance;
            var monitor = monitorFactory.Create(SCardScope.System);
            var readerName = "ACS ACR122 0";
            string cardUID = null;

            monitor.StatusChanged += (sender, states) =>
            {
                if (states.NewState == SCRState.Empty)
                {
                    cardUID = null;
                    Console.Clear();
                    Console.WriteLine("[Status] Card Remove");
                }
                else if (states.NewState == SCRState.Present)
                {
                    if (states.NewState == SCRState.InUse)
                    {
                        Console.WriteLine($"[Status] {states.NewState}");
                        return;
                    }
                    Console.WriteLine($"[Status] {states.NewState}");

                    using (var context = ContextFactory.Instance.Establish(SCardScope.System))
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(cardUID))
                            {
                                return;
                            }
                            using (var rfidReader = context.ConnectReader(readerName, SCardShareMode.Shared, SCardProtocol.Any))
                            {

                                var apdu = new CommandApdu(IsoCase.Case2Short, rfidReader.Protocol)
                                {
                                    CLA = 0xFF,
                                    Instruction = InstructionCode.GetData,
                                    P1 = 0x00,
                                    P2 = 0x00,
                                    Le = 0
                                };

                                using (rfidReader.Transaction(SCardReaderDisposition.Leave))
                                {
                                    //Console.WriteLine("Retrieving the UID .... ");

                                    var sendPci = SCardPCI.GetPci(rfidReader.Protocol);
                                    var receivePci = new SCardPCI();

                                    var receiveBuffer = new byte[256];
                                    var command = apdu.ToArray();

                                    var bytesReceived = rfidReader.Transmit(
                                        sendPci,
                                        command,
                                        command.Length,
                                        receivePci,
                                        receiveBuffer,
                                        receiveBuffer.Length);
                                    var responseApdu = new ResponseApdu(receiveBuffer, bytesReceived, IsoCase.Case2Short, rfidReader.Protocol);
                                    if (responseApdu.HasData)
                                    {
                                        cardUID = BitConverter.ToString(responseApdu.GetData()).Replace("-", string.Empty);
                                        string cardNo = CCardUtil.CardCode(cardUID);
                                        if (string.IsNullOrEmpty(cardNo))
                                        {
                                            Console.WriteLine("[Card] Unsupported Card");
                                            return;
                                        }
                                        Console.WriteLine("[Card] ID:" + cardNo);
                                        InputSimulator s = new InputSimulator();
                                        s.Keyboard.TextEntry(cardNo);
                                        Thread.Sleep(10);
                                        s.Keyboard.KeyDown(VirtualKeyCode.RETURN);
                                        Thread.Sleep(10);
                                        s.Keyboard.KeyUp(VirtualKeyCode.RETURN);
                                        Thread.Sleep(10);
                                    }
                                    else
                                    {
                                        cardUID = null;
                                        Console.WriteLine("[Card] No uid received");
                                    }

                                }
                            }
                        }
                        catch (PCSC.Exceptions.ReaderUnavailableException)
                        {
                            cardUID = null;
                            Console.WriteLine("[Card] Reader Unavailable");
                        }
                    }
                }
            };

            monitor.Start(readerName);

            Console.ReadKey();

            monitor.Cancel();
            monitor.Dispose();
        }
    }
}
