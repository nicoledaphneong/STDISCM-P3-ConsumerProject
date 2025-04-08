using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace ConsumerProject
{
    public class Consumer
    {
        private string rootFolderPath;
        private int maxQueueLength;
        private int port;

        public Consumer(string rootFolderPath, int maxQueueLength, int port)
        {
            this.rootFolderPath = rootFolderPath;
            this.maxQueueLength = maxQueueLength;
            this.port = port;
        }

        public void Start()
        {
            TcpListener listener = null;
            bool listenerStarted = false;

            while (!listenerStarted)
            {
                try
                {
                    listener = new TcpListener(IPAddress.Any, port);
                    listener.Start();
                    listenerStarted = true;
                    Console.WriteLine("Consumer is listening for video uploads...");
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    Console.WriteLine($"Port {port} is already in use. Please enter a different port number:");
                    port = int.Parse(Console.ReadLine());
                }
            }

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

                    // Read the header (threadId|fileName)
                    int headerLength = 0;
                    while ((bytesRead = stream.Read(buffer, headerLength, 1)) > 0)
                    {
                        if (buffer[headerLength] == 0) // Delimiter found
                        {
                            break;
                        }
                        headerLength++;
                    }

                    string header = System.Text.Encoding.UTF8.GetString(buffer, 0, headerLength);
                    string[] headerParts = header.Split('|');
                    int threadId = int.Parse(headerParts[0]);
                    string fileName = headerParts[1];

                    // Create or use existing folder for the thread
                    string threadFolderPath = Path.Combine(rootFolderPath, $"thread{threadId}");
                    Directory.CreateDirectory(threadFolderPath);

                    string filePath = Path.Combine(threadFolderPath, fileName);
                    filePath = GetUniqueFilePath(filePath);

                    // Read the file content
                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ms.Write(buffer, 0, bytesRead);
                    }

                    byte[] videoData = ms.ToArray();

                    // Simulate a leaky bucket (bounded queue)
                    if (Directory.GetFiles(threadFolderPath).Length < maxQueueLength)
                    {
                        File.WriteAllBytes(filePath, videoData);
                        Console.WriteLine($"Saved video to {filePath}.");
                        DisplayVideo(filePath);
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

        private string GetUniqueFilePath(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);
            int count = 1;

            while (File.Exists(filePath))
            {
                filePath = Path.Combine(directory, $"{fileNameWithoutExtension} ({count}){extension}");
                count++;
            }

            return filePath;
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

            string rootFolderPath;
            if (useDefaultPath.Equals("Y", StringComparison.OrdinalIgnoreCase))
            {
                rootFolderPath = AppDomain.CurrentDomain.BaseDirectory;
            }
            else
            {
                Console.WriteLine("Enter the folder path where videos will be saved:");
                rootFolderPath = Console.ReadLine();
            }

            Consumer consumer = new Consumer(rootFolderPath, maxQueueLength, port);
            consumer.Start();

            Console.WriteLine("Consumer started. Press any key to exit...");
            Console.ReadKey();
        }
    }
}
