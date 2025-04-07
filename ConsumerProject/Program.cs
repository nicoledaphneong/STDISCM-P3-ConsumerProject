using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace ConsumerProject
{
    public class Consumer
    {
        private string saveFolderPath;
        private int maxQueueLength;
        private int port;

        public Consumer(string saveFolderPath, int maxQueueLength, int port)
        {
            this.saveFolderPath = saveFolderPath;
            this.maxQueueLength = maxQueueLength;
            this.port = port;
        }

        public void Start()
        {
            TcpListener listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine("Consumer is listening for video uploads...");

            while (true)
            {
                // Accept a connection from a producer
                TcpClient client = listener.AcceptTcpClient();
                new Thread(() => HandleUpload(client)).Start();
            }
        }

        private void HandleUpload(TcpClient client)
        {
            try
            {
                using (NetworkStream stream = client.GetStream())
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buffer = new byte[1024];
                    int bytesRead;

                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ms.Write(buffer, 0, bytesRead);
                    }

                    byte[] videoData = ms.ToArray();
                    string fileName = Path.Combine(saveFolderPath, $"video_{DateTime.Now.Ticks}.mp4");

                    // Simulate a leaky bucket (bounded queue)
                    if (Directory.GetFiles(saveFolderPath).Length < maxQueueLength)
                    {
                        File.WriteAllBytes(fileName, videoData);
                        Console.WriteLine($"Saved video to {fileName}.");
                        DisplayVideo(fileName);
                    }
                    else
                    {
                        Console.WriteLine("Queue is full. Video dropped.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error receiving video: {ex.Message}");
            }
            finally
            {
                client.Close();
            }
        }

        private void DisplayVideo(string videoFilePath)
        {
            // This is a simple simulation for video display
            // In a real scenario, we would integrate a media library like VLC or use Windows Media Player for actual video display
            Console.WriteLine($"Displaying video {videoFilePath} (Previewing first 10 seconds)...");

            // Simulate video preview by waiting for 10 seconds
            Thread.Sleep(10000);
            Console.WriteLine($"Video {videoFilePath} previewed.");
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Enter the maximum queue length:");
            int maxQueueLength = int.Parse(Console.ReadLine());

            Console.WriteLine("Enter the port number for communication:");
            int port = int.Parse(Console.ReadLine());

            Console.WriteLine("Use default path (\"{0}\")? Y/n", AppDomain.CurrentDomain.BaseDirectory);
            string useDefaultPath = Console.ReadLine();

            string saveFolderPath;
            if (useDefaultPath.Equals("Y", StringComparison.OrdinalIgnoreCase))
            {
                saveFolderPath = AppDomain.CurrentDomain.BaseDirectory;
            }
            else
            {
                Console.WriteLine("Enter the folder path where videos will be saved:");
                saveFolderPath = Console.ReadLine();
            }

            Consumer consumer = new Consumer(saveFolderPath, maxQueueLength, port);
            consumer.Start();

            Console.WriteLine("Consumer started. Press any key to exit...");
            Console.ReadKey();
        }
    }
}
