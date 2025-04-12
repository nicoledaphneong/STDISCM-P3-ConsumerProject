using System;
using System.Windows.Forms;
using Vlc.DotNet.Forms;

namespace ConsumerProject
{
    public partial class Form1 : Form
    {
        private VlcControl vlcControl;
        private string videoFilePath;

        public Form1()
        {
            InitializeComponent();
            InitializeVLC();
        }

        private void InitializeVLC()
        {
            // Create a new instance of VlcControl (VLC Media Player)
            vlcControl = new VlcControl();
            vlcControl.Dock = DockStyle.Fill;

            // Add the control to the form
            this.Controls.Add(vlcControl);
        }

        private void LoadVideos()
        {
            string[] videoFiles = System.IO.Directory.GetFiles("C:\\PathToVideos", "*.mp4");

            foreach (var videoFile in videoFiles)
            {
                var pictureBox = new PictureBox
                {
                    Width = 100,
                    Height = 100,
                    SizeMode = PictureBoxSizeMode.StretchImage,
                    Image = CreateThumbnail(videoFile)
                };

                pictureBox.MouseHover += (sender, e) => StartPreview(videoFile);
                pictureBox.Click += (sender, e) => PlayVideo(videoFile);

                flowLayoutPanel.Controls.Add(pictureBox);
            }
        }

        private System.Drawing.Image CreateThumbnail(string videoFile)
        {
            // Create a thumbnail image for the video (you can use a static image or video thumbnail here)
            return System.Drawing.Image.FromFile("path_to_thumbnail_image.jpg");
        }

        private void StartPreview(string videoFile)
        {
            videoFilePath = videoFile;
            vlcControl.Play(new Uri(videoFilePath)); // Start previewing
        }

        private void PlayVideo(string videoFile)
        {
            videoFilePath = videoFile;
            vlcControl.Play(new Uri(videoFilePath)); // Play full video
        }

        private void playButton_Click(object sender, EventArgs e)
        {
            // Play the video when the play button is clicked
            if (!string.IsNullOrEmpty(videoFilePath))
            {
                vlcControl.Play(new Uri(videoFilePath));
            }
        }
    }
}
