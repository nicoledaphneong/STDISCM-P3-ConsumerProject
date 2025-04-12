using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Concurrent;

namespace ConsumerTest
{
    public class ConsumerGUI : Form
    {
        private FlowLayoutPanel videoPanel;
        private PictureBox currentPreviewBox;
        private Process currentPreviewProcess;
        private string rootFolderPath;
        private System.Windows.Forms.Timer updateTimer;
        private Dictionary<string, DateTime> lastPreviewTimes = new Dictionary<string, DateTime>();
        private bool previewInProgress = false;
        private bool isFullVideoPlaying = false;
        private BlockingCollection<TcpClient> uploadQueue = new BlockingCollection<TcpClient>();
        private int consumerThreadCount;

        public ConsumerGUI(string rootFolderPath)
        {
            this.rootFolderPath = rootFolderPath;
            InitializeGUI();
            StartUpdateTimer();
        }

        private void InitializeGUI()
        {
            this.Text = "Media Upload Service - Consumer";
            this.Size = new Size(1024, 768);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormClosing += (s, e) => StopAllPreviews();

            // Main panel for video thumbnails
            videoPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = true,
                BackColor = Color.FromArgb(45, 45, 48)
            };

            // Status bar
            var statusBar = new StatusStrip
            {
                Dock = DockStyle.Bottom,
                BackColor = Color.FromArgb(37, 37, 38)
            };
            var statusLabel = new ToolStripStatusLabel
            {
                Text = "Ready",
                ForeColor = Color.White
            };
            statusBar.Items.Add(statusLabel);

            this.Controls.Add(videoPanel);
            this.Controls.Add(statusBar);
        }

        private void StartUpdateTimer()
        {
            updateTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            updateTimer.Tick += (s, e) => UpdateVideoList();
            updateTimer.Start();
        }

        private void UpdateVideoList()
        {
            try
            {
                var videoFiles = Directory.GetFiles(rootFolderPath, "*.mp4", SearchOption.AllDirectories)
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .ToList();

                this.Invoke((MethodInvoker)delegate
                {
                    // Remove deleted videos
                    for (int i = videoPanel.Controls.Count - 1; i >= 0; i--)
                    {
                        var control = videoPanel.Controls[i];
                        if (control is Panel panel && panel.Controls[0] is PictureBox pb && !File.Exists(pb.Tag as string))
                        {
                            videoPanel.Controls.Remove(control);
                            control.Dispose();
                        }
                    }

                    // Add new videos
                    foreach (var file in videoFiles)
                    {
                        if (!videoPanel.Controls.Cast<Control>().Any(c => c.Controls[0].Tag as string == file))
                        {
                            AddVideoThumbnail(file);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating video list: {ex.Message}");
            }
        }

        private void AddVideoThumbnail(string filePath)
        {
            // Create a panel to hold the thumbnail and the label
            var thumbnailPanel = new Panel
            {
                Size = new Size(240, 200), // Adjust height to accommodate the label
                BackColor = Color.FromArgb(63, 63, 70),
                Margin = new Padding(10)
            };

            var thumbnail = new PictureBox
            {
                Size = new Size(240, 180),
                SizeMode = PictureBoxSizeMode.Zoom,
                Tag = filePath,
                BackColor = Color.FromArgb(63, 63, 70),
                Cursor = Cursors.Hand
            };

            var fileNameLabel = new Label
            {
                Text = Path.GetFileNameWithoutExtension(filePath),
                ForeColor = Color.White,
                Dock = DockStyle.Bottom,
                TextAlign = ContentAlignment.MiddleCenter,
                Height = 20
            };

            // Add mouse enter/leave events for preview
            thumbnail.MouseEnter += (s, e) => StartVideoPreview(filePath, thumbnail);
            thumbnail.MouseLeave += (s, e) => StopVideoPreview(thumbnail);

            // Add the thumbnail and label to the panel
            thumbnailPanel.Controls.Add(thumbnail);
            thumbnailPanel.Controls.Add(fileNameLabel);

            // Add to panel immediately with placeholder
            CreatePlaceholderThumbnail(thumbnail, filePath);
            videoPanel.Controls.Add(thumbnailPanel);

            // Then start async loading of real thumbnail
            _ = LoadThumbnailAsync(thumbnail, filePath);

            thumbnail.Click += (s, e) => PlayFullVideo(filePath);

        }


        private async Task LoadThumbnailAsync(PictureBox thumbnail, string filePath)
        {
            try
            {
                string thumbPath = Path.ChangeExtension(filePath, ".jpg");

                await Task.Run(() =>
                {
                    if (!File.Exists(thumbPath) || File.GetLastWriteTime(thumbPath) < File.GetLastWriteTime(filePath))
                    {
                        using (Process ffmpeg = new Process())
                        {
                            ffmpeg.StartInfo = new ProcessStartInfo
                            {
                                FileName = "ffmpeg",
                                Arguments = $"-y -i \"{filePath}\" -ss 00:00:01.000 -vframes 1 -q:v 2 \"{thumbPath}\"",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            ffmpeg.Start();
                            ffmpeg.WaitForExit(5000);
                        }
                    }
                });

                if (File.Exists(thumbPath) && thumbnail.IsHandleCreated)
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        // Only replace if still in the panel
                        if (videoPanel.Controls.Contains(thumbnail.Parent))
                        {
                            using (var tempImage = Image.FromFile(thumbPath))
                            {
                                thumbnail.Image?.Dispose(); // Clean up previous image
                                thumbnail.Image = new Bitmap(tempImage);
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading thumbnail: {ex.Message}");
            }
        }

        private async Task GenerateThumbnailImageAsync(PictureBox thumbnail, string filePath)
        {
            string thumbPath = Path.ChangeExtension(filePath, ".jpg");

            try
            {
                await Task.Run(() =>
                {
                    // Only generate if thumbnail doesn't exist or is older than video
                    if (!File.Exists(thumbPath) || File.GetLastWriteTime(thumbPath) < File.GetLastWriteTime(filePath))
                    {
                        using (Process ffmpeg = new Process())
                        {
                            ffmpeg.StartInfo = new ProcessStartInfo
                            {
                                FileName = "ffmpeg",
                                Arguments = $"-y -i \"{filePath}\" -ss 00:00:01.000 -vframes 1 -q:v 2 \"{thumbPath}\"",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            ffmpeg.Start();
                        }
                    }
                });

                if (File.Exists(thumbPath))
                {
                    // UI updates must be on main thread
                    this.Invoke((MethodInvoker)delegate
                    {
                        using (var tempImage = Image.FromFile(thumbPath))
                        {
                            thumbnail.Image = new Bitmap(tempImage);
                        }
                    });
                }
            }
            catch
            {
                this.Invoke((MethodInvoker)delegate
                {
                    CreatePlaceholderThumbnail(thumbnail, filePath);
                });
            }
        }

        private void CreatePlaceholderThumbnail(PictureBox thumbnail, string fileName)
        {
            Bitmap bmp = new Bitmap(thumbnail.Width, thumbnail.Height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Gray);

                using (Font font = new Font("Arial", 10, FontStyle.Bold))
                using (StringFormat sf = new StringFormat()
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                {
                    string displayText = Path.GetFileNameWithoutExtension(fileName);
                    g.DrawString(displayText, font, Brushes.White, new RectangleF(0, 0, bmp.Width, bmp.Height), sf);
                }
            }

            thumbnail.Image = bmp;
        }

        private DateTime lastHoverTime;
        private string lastHoverPath;

        private void StartVideoPreview(string filePath, PictureBox thumbnail)
        {
            if (previewInProgress || string.IsNullOrEmpty(filePath)) return;

            // Normalize path for tracking
            string normalizedPath = Path.GetFullPath(filePath).ToLowerInvariant();

            // Skip if already hovered recently
            if (normalizedPath == lastHoverPath && (DateTime.Now - lastHoverTime).TotalMilliseconds < 100)
            {
                Debug.WriteLine("Skipping preview: duplicate hover on same file");
                return;
            }

            previewInProgress = true;
            lastHoverPath = normalizedPath;
            lastHoverTime = DateTime.Now;

            Debug.WriteLine($"StartVideoPreview triggered for {normalizedPath}");

            StopAllPreviews();

            thumbnail.BorderStyle = BorderStyle.Fixed3D;
            thumbnail.BackColor = Color.FromArgb(0, 122, 204);
            currentPreviewBox = thumbnail;
            lastPreviewTimes[normalizedPath] = DateTime.Now;

            Task.Delay(300).ContinueWith(t =>
            {
                if (thumbnail.IsDisposed || !thumbnail.IsHandleCreated)
                {
                    previewInProgress = false;
                    return;
                }

                this.Invoke((MethodInvoker)delegate
                {
                    if (!thumbnail.ClientRectangle.Contains(thumbnail.PointToClient(Cursor.Position)))
                    {
                        Debug.WriteLine("Cursor no longer hovering thumbnail. Preview canceled.");
                        previewInProgress = false;
                        return;
                    }

                    Debug.WriteLine("Launching ffplay preview...");

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "ffplay",
                        Arguments = $"-i \"{filePath}\" -t 10 -autoexit -noborder -loglevel quiet -window_title \"Preview: {Path.GetFileNameWithoutExtension(filePath)}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    currentPreviewProcess = new Process
                    {
                        StartInfo = startInfo,
                        EnableRaisingEvents = true
                    };

                    currentPreviewProcess.OutputDataReceived += (sender, args) => Debug.WriteLine($"Output: {args.Data}");
                    currentPreviewProcess.ErrorDataReceived += (sender, args) => Debug.WriteLine($"Error: {args.Data}");
                    currentPreviewProcess.Exited += (sender, args) =>
                    {
                        Debug.WriteLine("Preview process exited.");
                        previewInProgress = false;
                    };

                    try
                    {
                        currentPreviewProcess.Start();
                        currentPreviewProcess.BeginOutputReadLine();
                        currentPreviewProcess.BeginErrorReadLine();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to start preview: {ex.Message}");
                        previewInProgress = false;
                    }
                });
            });
        }



        private DateTime lastMouseEnterTime;
        private PictureBox lastEnteredThumbnail;

        private async void Thumbnail_MouseEnter(object sender, EventArgs e)
        {
            var thumbnail = (PictureBox)sender;
            var filePath = thumbnail.Tag as string;

            Debug.WriteLine($"Thumbnail_MouseEnter called for {filePath}");

            // Debounce rapid mouse movements
            if ((DateTime.Now - lastMouseEnterTime).TotalMilliseconds < 100
                && lastEnteredThumbnail == thumbnail)
            {
                Debug.WriteLine("Skipping MouseEnter: rapid movement debounce");
                return;
            }

            lastMouseEnterTime = DateTime.Now;
            lastEnteredThumbnail = thumbnail;

            // Immediate visual feedback
            thumbnail.BorderStyle = BorderStyle.Fixed3D;
            thumbnail.BackColor = Color.FromArgb(0, 122, 204);


            // Verify we're still hovering after delay
            if (thumbnail.ClientRectangle.Contains(thumbnail.PointToClient(Cursor.Position)))
            {
                Debug.WriteLine("Starting video preview after delay");
                StartVideoPreview(filePath, thumbnail);
            }
            else
            {
                Debug.WriteLine("Mouse left thumbnail before delay ended");
            }
        }

        private void Thumbnail_MouseLeave(object sender, EventArgs e)
        {
            var thumbnail = (PictureBox)sender;
            Debug.WriteLine($"Thumbnail_MouseLeave called for {thumbnail.Tag as string}");

            // Stop the preview only if a full video is not playing
            if (isFullVideoPlaying)
            {
                Debug.WriteLine("Skip stopping preview because full video is playing.");
                return;
            }
        }


        private void StopVideoPreview(PictureBox thumbnail)
        {
            Debug.WriteLine($"StopVideoPreview called for {thumbnail.Tag as string}");

            if (currentPreviewBox == thumbnail)
            {
                thumbnail.BorderStyle = BorderStyle.FixedSingle;
                thumbnail.BackColor = Color.FromArgb(63, 63, 70);
                currentPreviewBox = null;
            }

            if (currentPreviewProcess != null && !currentPreviewProcess.HasExited)
            {
                try
                {
                    currentPreviewProcess.Kill();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error killing preview process: {ex.Message}");
                }
                currentPreviewProcess.Dispose();
                currentPreviewProcess = null;
            }

            // Ensure this always gets reset
            previewInProgress = false;
        }


        private void StopAllPreviews()
        {
            foreach (Control control in videoPanel.Controls)
            {
                if (control is PictureBox pb)
                {
                    pb.BorderStyle = BorderStyle.FixedSingle;
                    pb.BackColor = Color.FromArgb(63, 63, 70);
                }
            }

            if (currentPreviewProcess != null && !currentPreviewProcess.HasExited)
            {
                currentPreviewProcess.Kill();
                currentPreviewProcess = null;
            }
        }

        private void PlayFullVideo(string filePath)
        {
            try
            {
                Debug.WriteLine($"PlayFullVideo called for {filePath}");

                // Stop any existing preview process
                StopAllPreviews();

                this.Invoke((MethodInvoker)delegate
                {
                    Debug.WriteLine("Launching full video playback process");

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "ffplay",
                        Arguments = $"-i \"{filePath}\" -window_title \"Now playing: {Path.GetFileNameWithoutExtension(filePath)}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    currentPreviewProcess = new Process
                    {
                        StartInfo = startInfo,
                        EnableRaisingEvents = true
                    };

                    currentPreviewProcess.OutputDataReceived += (sender, args) => Debug.WriteLine($"Output: {args.Data}");
                    currentPreviewProcess.ErrorDataReceived += (sender, args) => Debug.WriteLine($"Error: {args.Data}");
                    currentPreviewProcess.Exited += (sender, args) =>
                    {
                        Debug.WriteLine("Playback process exited");
                        isFullVideoPlaying = false; // Reset the flag when the video finishes

                        // Optional: Restore title
                        this.Invoke((MethodInvoker)(() => this.Text = "Media Upload Service - Consumer"));
                    };

                    Debug.WriteLine($"Starting process with arguments: {startInfo.Arguments}");
                    currentPreviewProcess.Start();
                    currentPreviewProcess.BeginOutputReadLine();
                    currentPreviewProcess.BeginErrorReadLine();

                    // Optional: Change the app title too
                    this.Text = $"Now playing: {Path.GetFileNameWithoutExtension(filePath)}";
                    isFullVideoPlaying = true; // Set the flag when the video starts playing
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error starting full video playback: {ex.Message}");
            }
        }
    }

    public class Consumer
    {
        private string rootFolderPath;
        private int maxQueueLength;
        private int port;
        private int consumerThreadCount;
        private ConsumerGUI gui;
        private BlockingCollection<TcpClient> uploadQueue = new BlockingCollection<TcpClient>();

        public Consumer(string rootFolderPath, int maxQueueLength, int port, int consumerThreadCount)
        {
            this.rootFolderPath = rootFolderPath;
            this.maxQueueLength = maxQueueLength;
            this.port = port;
            this.consumerThreadCount = consumerThreadCount;

            // Initialize the BlockingCollection with a bounded capacity
            this.uploadQueue = new BlockingCollection<TcpClient>(maxQueueLength);

            Thread guiThread = new Thread(() =>
            {
                gui = new ConsumerGUI(rootFolderPath);
                Application.Run(gui);
            });
            guiThread.SetApartmentState(ApartmentState.STA);
            guiThread.Start();
        }

        public void Start()
        {
            TcpListener? listener = null;
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
                    string? input = Console.ReadLine();
                    if (!int.TryParse(input, out port))
                    {
                        Console.WriteLine("Invalid port number. Using default port 8080.");
                        port = 8080;
                    }
                }
            }

            // DEMO 5
            // Start consumer threads
            for (int i = 0; i < consumerThreadCount; i++)
            {
                new Thread(UploadWorker).Start();
            }

            while (true)
            {
                TcpClient client = listener.AcceptTcpClient();
                try
                {
                    // DEMO 6
                    // Add the client to the queue, this will block if the queue is full
                    uploadQueue.Add(client);
                }
                catch (InvalidOperationException)
                {
                    // This exception is thrown if the collection has been marked as complete for adding
                    Console.WriteLine("Queue has been marked as complete for adding.");
                    break;
                }
            }
        }

        private void UploadWorker()
        {
            foreach (var client in uploadQueue.GetConsumingEnumerable())
            {
                try
                {
                    HandleUpload(client);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Worker error: {ex.Message}");
                }
            }
        }

        // DEMO 4:
        // Reads file header, file data then saves it
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
                        if (buffer[headerLength] == 0)
                            break;
                        headerLength++;
                    }

                    string header = System.Text.Encoding.UTF8.GetString(buffer, 0, headerLength);
                    string[] headerParts = header.Split('|');
                    if (headerParts.Length < 2)
                        throw new InvalidDataException("Invalid header format");

                    if (!int.TryParse(headerParts[0], out int threadId))
                        throw new InvalidDataException("Invalid thread ID format");

                    string fileName = headerParts[1] ?? throw new InvalidDataException("File name cannot be null");

                    string threadFolderPath = Path.Combine(rootFolderPath, $"thread{threadId}");
                    Directory.CreateDirectory(threadFolderPath);

                    string filePath = Path.Combine(threadFolderPath, fileName);
                    filePath = GetUniqueFilePath(filePath);

                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ms.Write(buffer, 0, bytesRead);
                    }

                    byte[] videoData = ms.ToArray();

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
            string? directory = Path.GetDirectoryName(filePath);
            if (directory == null)
                throw new ArgumentException("Invalid file path", nameof(filePath));

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
            Console.WriteLine($"Displaying video {videoFilePath} (Previewing first 10 seconds)...");
            Thread.Sleep(10000);
            Console.WriteLine($"Video {videoFilePath} previewed.");
        }
    }

    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine("Enter the maximum queue length:");
            int maxQueueLength = int.Parse(Console.ReadLine() ?? "5");

            Console.WriteLine("Enter the port number for communication:");
            int port = int.Parse(Console.ReadLine() ?? "8080");

            Console.WriteLine("Enter the number of consumer threads:");
            int threadCount = int.Parse(Console.ReadLine() ?? "5");

            Console.WriteLine("Use default path (\"{0}\")? Y/n", AppDomain.CurrentDomain.BaseDirectory);
            string useDefaultPath = Console.ReadLine() ?? "Y";

            string rootFolderPath = useDefaultPath.Equals("Y", StringComparison.OrdinalIgnoreCase)
                ? AppDomain.CurrentDomain.BaseDirectory
                : Console.ReadLine() ?? AppDomain.CurrentDomain.BaseDirectory;

            Consumer consumer = new Consumer(rootFolderPath, maxQueueLength, port, threadCount);
            consumer.Start();

            Console.WriteLine("Consumer started. GUI is running...");
            Console.ReadLine();
        }
    }
}