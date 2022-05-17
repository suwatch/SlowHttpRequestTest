
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SlowHttpRequestTest
{
    internal class Program
    {
        static Random _random = new Random();
        static Uri _uri;

        static void Main(string[] args)
        {
            if (args.Length <= 0 || !Uri.TryCreate(args[0], UriKind.Absolute, out _uri) || _uri.Scheme != "http")
            {
                Console.WriteLine($"Usage: {nameof(SlowHttpRequestTest)}.exe uri");
                return;
            }

            var activityId = 100000;
            Tuple<bool, bool> result = new Tuple<bool, bool>(false, false);
            var batches = new[] { 1, 5, 10, 15, 20 };
            while (true)
            {
                foreach (var sendBatch in batches)
                {
                    var sent = false;
                    var received = false;
                    foreach (var recBatch in batches)
                    {
                        ++activityId;
                        result = TestSocket($"00{activityId}-0000-0000-0000-000000000000", sendBatch, recBatch);
                        sent = result.Item1;
                        received = result.Item2;

                        if (!sent)
                            break;

                        if (received)
                            break;
                    }

                    if (sent && received)
                    {
                        break;
                    }
                }
            }
        }

        static Tuple<bool, bool> TestSocket(string activityId, int sendBatch, int recBatch)
        {
            bool sendSuccess = false, recSuccess = false;
            try
            {
                var hostName = _uri.Host;
                if (!IPAddress.TryParse(hostName, out var addr))
                {
                    var hostEntry = Dns.GetHostEntry(hostName);
                    addr = hostEntry.AddressList[0];
                }

                var sendLength = sendBatch * 1500;
                var strb = new StringBuilder();
                while (strb.Length < sendLength)
                {
                    strb.AppendLine($"{Guid.NewGuid()}");
                }

                var payload = $@"POST {_uri.PathAndQuery} HTTP/1.1
Host: {hostName}
Accept: */*
Content-Type: text/plain
Content-Length: {strb.Length}
x-ms-request-id: {activityId}
User-Agent: SlowHttpRequestTest/1.0

{strb}";

                // Ref: https://docs.microsoft.com/en-us/iis/configuration/system.applicationhost/weblimits#attributes
                // Ref: https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.server.kestrel.core.kestrelserverlimits.minrequestbodydatarate?view=aspnetcore-6.0
                // Ref: https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.server.kestrel.core.kestrelserverlimits.minresponsedatarate?view=aspnetcore-6.0
                var bytes = Encoding.UTF8.GetBytes(payload);
                using (var socket = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp) 
                {
                    SendTimeout = (int)TimeSpan.FromMinutes(10).TotalMilliseconds,
                    ReceiveTimeout = (int)TimeSpan.FromMinutes(10).TotalMilliseconds,
                    SendBufferSize = sendBatch, 
                    ReceiveBufferSize = recBatch 
                })
                {
                    socket.Connect(new IPEndPoint(addr, 80));

                    var traceTime = DateTime.MinValue;
                    var total = 0;
                    Console.Write($"{DateTime.UtcNow:s} === Request Rate: {sendBatch * 10,3} bytes/sec, activityId: {activityId} ===");
                    for (int i = 0; i < bytes.Length; i += sendBatch)
                    {
                        var toSend = (i + sendBatch > bytes.Length) ? (bytes.Length - i) : sendBatch;
                        total += socket.Send(bytes, i, toSend, SocketFlags.None);
                        if (traceTime < DateTime.UtcNow)
                        {
                            traceTime = DateTime.UtcNow.AddSeconds(30);
                            Console.Write($"{total}.");
                        }
                        Thread.Sleep(100);
                    }
                    Console.WriteLine($" {total} bytes successful");
                    sendSuccess = true;

                    traceTime = DateTime.MinValue;
                    total = 0;
                    byte[] rec = new byte[recBatch];
                    var recTime = 0;
                    Console.Write($"{DateTime.UtcNow:s} === Response Rate {recBatch * 10,3} bytes/sec ===");
                    while (true)
                    {
                        var got = socket.Receive(rec);
                        //Console.WriteLine(Encoding.UTF8.GetString(rec, 0, got));
                        total += got;
                        if (traceTime < DateTime.UtcNow)
                        {
                            traceTime = DateTime.UtcNow.AddSeconds(30);
                            Console.Write($"{total}.");
                            ++recTime;
                        }

                        if (total > 5_000 || recTime > 10)
                        {
                            break;
                        }

                        Thread.Sleep(100);
                    }
                    Console.WriteLine($" {total} bytes successful");
                    socket.Close();
                    recSuccess = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{DateTime.UtcNow:s} {ex.Message}");
            }

            return new Tuple<bool, bool>(sendSuccess, recSuccess);
        }
    }
}
