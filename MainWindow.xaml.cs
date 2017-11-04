//------------------------------------------------------------------------------
// <copyright file="MainWindow.xaml.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Microsoft.Samples.Kinect.DepthBasics
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Windows;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;
    using Microsoft.Kinect;
    using System.Windows.Controls;
    using System.Collections.Generic;
    using System.Windows.Input;

    /// <summary>
    /// Interaction logic for MainWindow
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        /// <summary>
        /// Map depth range to byte range
        /// </summary>
        private const int MapDepthToByte = 8000 / 256;

        /// <summary>
        /// Active Kinect sensor
        /// </summary>
        private KinectSensor kinectSensor = null;

        /// <summary>
        /// Reader for depth frames
        /// </summary>
        private DepthFrameReader depthFrameReader = null;

        /// <summary>
        /// Description of the data contained in the depth frame
        /// </summary>
        private FrameDescription depthFrameDescription = null;

        /// <summary>
        /// Bitmap to display
        /// </summary>
        private WriteableBitmap depthBitmap = null;

        /// <summary>
        /// Intermediate storage for frame data converted to color
        /// </summary>
        private byte[] depthPixels = null;

        /// <summary>
        /// Frame data for specific Y coord, viewed from above
        /// </summary>
        private byte[] depthPixelsY = null;

        private PointCloudVertex[] ptc;

        /// <summary>
        /// depth limits and color values for image segmentation
        /// </summary>
        private Dictionary<int, Color> imageSegmentColors = new Dictionary<int, Color>(){
                                                                {0,Color.FromRgb(0,0,0)}, //black
                                                                {1, Color.FromRgb(184,134,11)}, //dark goldenrod
                                                                {2,Color.FromRgb(0,139,139)}, //dark cyan
                                                                {3,Color.FromRgb(199,21,112)}, //medium violet red
                                                                {4,Color.FromRgb(50,205,50)}, //lime green
                                                                {5,Color.FromRgb(255,165,0)}, //orange
                                                                {6,Color.FromRgb(253,99,71)}, //tomato
                                                                {7,Color.FromRgb(240,255,255)}, //azure
                                                                {8,Color.FromRgb(210,105,30)}, //chocolate
                                                                {9,Color.FromRgb(255,0,255)}, //fuschia
                                                                {10,Color.FromRgb(255,215,0)}, //gold
                                                                {11,Color.FromRgb(255,0,0)}, //red
                                                                {12,Color.FromRgb(64,224,208)}, //turquoise
                                                                {13,Color.FromRgb(154,205,50)}, //yellowgreen
                                                                {14,Color.FromRgb(255,255,205)}, //blanched almond
                                                                {15,Color.FromRgb(100,149,237)}, //cornflowerblue
                                                                {16,Color.FromRgb(128,0,0)}, //maroon
                                                                {17,Color.FromRgb(128,0,128)}, //OrangeRed
                                                                {18,Color.FromRgb(0,128,0)}, //Green
                                                            };

        /// <summary>
        /// Current status text to display
        /// </summary>
        private string statusText = null;

        /// <summary>
        /// Y coord to draw dept visualization
        /// </summary>
        private int depthCutoff;

        /// <summary>
        /// Has the current frame been segmented
        /// </summary>
        private bool firstFrame = true;

        /// <summary>
        /// Initializes a new instance of the MainWindow class.
        /// </summary>
        public MainWindow()
        {
            // get the kinectSensor object
            this.kinectSensor = KinectSensor.GetDefault();

            // open the reader for the depth frames
            this.depthFrameReader = this.kinectSensor.DepthFrameSource.OpenReader();

            // wire handler for frame arrival
            this.depthFrameReader.FrameArrived += this.Reader_FrameArrived;

            // get FrameDescription from DepthFrameSource
            this.depthFrameDescription = this.kinectSensor.DepthFrameSource.FrameDescription;

            // allocate space to put the pixels being received and converted
            this.depthPixels = new byte[this.depthFrameDescription.Width * this.depthFrameDescription.Height];

            this.depthPixelsY = new byte[this.depthFrameReader.DepthFrameSource.DepthMaxReliableDistance * this.depthFrameDescription.Width];

            this.ptc = new PointCloudVertex[this.depthFrameDescription.Width * this.depthFrameDescription.Height];


            // create the bitmap to display
            this.depthBitmap = new WriteableBitmap(this.depthFrameDescription.Width, this.depthFrameDescription.Height, 96.0, 96.0, PixelFormats.Gray8, null);

            // set IsAvailableChanged event notifier
            this.kinectSensor.IsAvailableChanged += this.Sensor_IsAvailableChanged;

            // open the sensor
            this.kinectSensor.Open();

            // set the status text
            this.StatusText = this.kinectSensor.IsAvailable ? Properties.Resources.RunningStatusText
                                                            : Properties.Resources.NoSensorStatusText;

            // use the window object as the view model in this simple example
            this.DataContext = this;

            // initialize the components (controls) of the window
            this.InitializeComponent();
        }

        /// <summary>
        /// INotifyPropertyChangedPropertyChanged event to allow window controls to bind to changeable data
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Gets the bitmap to display
        /// </summary>
        public ImageSource ImageSource
        {
            get
            {
                return this.depthBitmap;
            }
        }

        /// <summary>
        /// Gets or sets the current status text to display
        /// </summary>
        public string StatusText
        {
            get
            {
                return this.statusText;
            }

            set
            {
                if (this.statusText != value)
                {
                    this.statusText = value;

                    // notify any bound elements that the text has changed
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("StatusText"));
                    }
                }
            }
        }

        /// <summary>
        /// Execute shutdown tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (this.depthFrameReader != null)
            {
                // DepthFrameReader is IDisposable
                this.depthFrameReader.Dispose();
                this.depthFrameReader = null;
            }

            if (this.kinectSensor != null)
            {
                this.kinectSensor.Close();
                this.kinectSensor = null;
            }
        }

        private void Image_MouseDown(object sender, MouseEventArgs e)
        {
            int i = (int) (this.depthBitmap.PixelWidth * e.GetPosition(this).Y + e.GetPosition(this).X);
            Debug.WriteLine(e.GetPosition(this).X + ", " + e.GetPosition(this).Y + " : "+ptc[i].ToString());
        }

        /// <summary>
        /// Handles the user clicking on the screenshot button
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void ScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.depthBitmap != null)
            {
                // create a png bitmap encoder which knows how to save a .png file
                BitmapEncoder encoder = new PngBitmapEncoder();

                // create frame from the writable bitmap and add to encoder
                encoder.Frames.Add(BitmapFrame.Create(this.depthBitmap));

                string time = System.DateTime.UtcNow.ToString("hh'-'mm'-'ss", CultureInfo.CurrentUICulture.DateTimeFormat);

                string myPhotos = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

                string path = Path.Combine(myPhotos, "KinectScreenshot-Depth-" + time + ".png");

                // write the new file to disk
                try
                {
                    // FileStream is IDisposable
                    using (FileStream fs = new FileStream(path, FileMode.Create))
                    {
                        encoder.Save(fs);
                    }

                    this.StatusText = string.Format(CultureInfo.CurrentCulture, Properties.Resources.SavedScreenshotStatusTextFormat, path);
                }
                catch (IOException)
                {
                    this.StatusText = string.Format(CultureInfo.CurrentCulture, Properties.Resources.FailedScreenshotStatusTextFormat, path);
                }
            }
        }

        /// <summary>
        /// Handles the depth frame data arriving from the sensor
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void Reader_FrameArrived(object sender, DepthFrameArrivedEventArgs e)
        {
            if (firstFrame)
            {
                bool depthFrameProcessed = false;

                using (DepthFrame depthFrame = e.FrameReference.AcquireFrame())
                {
                    if (depthFrame != null)
                    {

                        // the fastest way to process the body index data is to directly access 
                        // the underlying buffer
                        using (Microsoft.Kinect.KinectBuffer depthBuffer = depthFrame.LockImageBuffer())
                        {
                            // verify data and write the color data to the display bitmap
                            if (((this.depthFrameDescription.Width * this.depthFrameDescription.Height) == (depthBuffer.Size / this.depthFrameDescription.BytesPerPixel))
                                && (this.depthFrameDescription.Width == this.depthBitmap.PixelWidth) && (this.depthFrameDescription.Height == this.depthBitmap.PixelHeight))
                            {
                                // Note: In order to see the full range of depth (including the less reliable far field depth)
                                // we are setting maxDepth to the extreme potential depth threshold
                                ushort maxDepth = ushort.MaxValue;

                                // If you wish to filter by reliable depth distance, uncomment the following line:
                                //maxDepth = depthFrame.DepthMaxReliableDistance;

                                this.ProcessDepthFrameData(depthBuffer.UnderlyingBuffer, depthBuffer.Size, depthFrame.DepthMinReliableDistance, maxDepth);
                                depthFrameProcessed = true;
                                firstFrame = false;
                            }
                        }
                    }
                }

                if (depthFrameProcessed)
                {
                    this.RenderDepthPixels();
                }

            }
        }

        /// <summary>
        /// Directly accesses the underlying image buffer of the DepthFrame to 
        /// create a displayable bitmap.
        /// This function requires the /unsafe compiler option as we make use of direct
        /// access to the native memory pointed to by the depthFrameData pointer.
        /// </summary>
        /// <param name="depthFrameData">Pointer to the DepthFrame image data</param>
        /// <param name="depthFrameDataSize">Size of the DepthFrame image data</param>
        /// <param name="minDepth">The minimum reliable depth value for the frame</param>
        /// <param name="maxDepth">The maximum reliable depth value for the frame</param>
        private unsafe void ProcessDepthFrameData(IntPtr depthFrameData, uint depthFrameDataSize, ushort minDepth, ushort maxDepth)
        {
         
            
            // depth frame data is a 16 bit value
            ushort* frameData = (ushort*)depthFrameData;
            int width = this.depthBitmap.PixelWidth;
            int height = this.depthBitmap.PixelHeight;

            for (int x = 0; x < width; ++x)
            {
                for (int y = 0; y < height; ++y)
                {
                    int i = width * y + x;
                    ushort depth = frameData[i];
                    /**
                    double theta = (2 * x - width) / this.depthFrameDescription.HorizontalFieldOfView;
                    double phi = (height - 2 * y) / this.depthFrameDescription.VerticalFieldOfView;
                    double newX = depth * Math.Cos(theta) * Math.Sin(phi);
                    double newY = depth * Math.Sin(theta) * Math.Sin(phi);
                    double newZ = depth * Math.Cos(phi);
                    this.ptc[i] = new PointCloudVertex(newX/1000, newY/1000, newZ/1000);
                    **/
                    if (i > width)
                    {
                        depthPixels[i] = (byte)Math.Abs(depth - depthPixels[i - width]);
                    }
                }
            }
            
            

        }
        /// <summary>
        /// Renders color pixels into the writeableBitmap.
        /// </summary>
        private void RenderDepthPixels()
        {
            this.depthBitmap.WritePixels(
                new Int32Rect(0, 0, this.depthBitmap.PixelWidth, this.depthBitmap.PixelHeight),
                this.depthPixels,
                this.depthBitmap.PixelWidth,
                0);
           // WritePointCloudToPCD();
        }

        private void WritePointCloudToPCD()
        {
            String header = "VERSION .7 \n" +
                            "FIELDS x y z\n" +
                            "SIZE 8 8 8\n" +
                            "TYPE I " +
                            "WIDTH " + this.depthBitmap.PixelWidth + "\n" +
                            "HEIGHT " + this.depthBitmap.PixelHeight + "\n" +
                            "VIEWPOINT 0 0 0 1 0 0 0 \n" +
                            "POINTS " + this.ptc.Length +
                            "DATA ascii \n";
            String data = "";
            for(int i = 0; i < ptc.Length; i++)
            {
                Debug.WriteLine("Index " + i + " of " + ptc.Length);
                data += ptc[i].ToString() +"\n";
            }
            System.IO.File.WriteAllText(@"D:\Dropbox\DepthBasics-WPF\ptc.pcd", header + data);
            Debug.WriteLine("Point cloud completed");
        }

        /// <summary>
        /// Handles the event which the sensor becomes unavailable (E.g. paused, closed, unplugged).
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void Sensor_IsAvailableChanged(object sender, IsAvailableChangedEventArgs e)
        {
            // on failure, set the status text
            this.StatusText = this.kinectSensor.IsAvailable ? Properties.Resources.RunningStatusText
                                                            : Properties.Resources.SensorNotAvailableStatusText;
        }
    }
    class PointCloudVertex
    {
        double x;
        double y;
        double z;

        public PointCloudVertex(double x, double y, double z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        override public String ToString()
        {
            return ""+x+" " + y + " " + z;
        }
    }
}
