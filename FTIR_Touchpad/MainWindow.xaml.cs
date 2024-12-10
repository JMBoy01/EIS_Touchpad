using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using Emgu.CV;
using Emgu.CV.Structure;
using System.Runtime.InteropServices;
using System.Diagnostics.Tracing;
using Emgu.CV.Flann;

namespace FTIR_Touchpad
{
    public partial class MainWindow : Window
    {
        // https://stackoverflow.com/questions/1316681/getting-mouse-position-in-c-sharp
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;

            public static implicit operator System.Drawing.Point(POINT point)
            {
                return new System.Drawing.Point(point.X, point.Y);
            }
        }
        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int X, int Y);
        
        // Constants voor muisacties
        private const int MOUSEEVENTF_LEFTDOWN = 0x02;
        private const int MOUSEEVENTF_LEFTUP = 0x04;
        [DllImport("user32.dll")]
        private static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

        private VideoCapture _capture;
        private Mat _frame;

        private Emgu.CV.UI.ImageBox imageBox;

        private double threshold;

        private System.Drawing.Point previous_position = System.Drawing.Point.Empty;
        private int frame_count = 0;
        private int click_frame_count_threshold = 5;

        public MainWindow()
        {
            InitializeComponent();

            InitializeUI();

            _capture = new VideoCapture(0);
            _frame = new Mat();

            _capture.ImageGrabbed += Capture_ImageGrabbed;
            _capture.Start();

        }

        private void InitializeUI()
        {
            WindowsFormsHost host = new WindowsFormsHost{Name="FormHost", HorizontalAlignment=System.Windows.HorizontalAlignment.Left, VerticalAlignment=System.Windows.VerticalAlignment.Top};
            imageBox = new Emgu.CV.UI.ImageBox{Name="CameraView", FunctionalMode=Emgu.CV.UI.ImageBox.FunctionalModeOption.Minimum};
            host.Child = imageBox;

            Slider slider = new Slider
            {
                Name = "ThresholdSlider",
                Minimum = 0,
                Maximum = 255,
                Value = 255, // Standaardwaarde
                Height = 280,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Margin = new Thickness(15) // Ruimte vanaf de randen
            };
            slider.ValueChanged += Slider_ValueChanged;

            MainGrid.Children.Add(host);
            MainGrid.Children.Add(slider);
        }

        private void Capture_ImageGrabbed(object sender, EventArgs e)
        {
            // Lees het frame in
            _capture.Retrieve(_frame);

            // TODO doe hier de vingers nog detecteren, ...
            Mat image = ProcessImage(_frame);

            imageBox.Image = image;
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Haal de huidige waarde van de slider op
            Slider slider = sender as Slider;
            if (slider != null)
            {
                threshold = slider.Value;
            }
        }

        public static System.Drawing.Point GetCursorPosition() // https://stackoverflow.com/questions/1316681/getting-mouse-position-in-c-sharp
        {
            POINT lpPoint;
            GetCursorPos(out lpPoint);
            return lpPoint;
        }

        private Mat ProcessImage(Mat image)
        {
            Mat resultImage = new Mat{};

            // Converteer image naar grijs en pas thresholding toe
            CvInvoke.CvtColor(image, resultImage, Emgu.CV.CvEnum.ColorConversion.Bgr2Gray);
            CvInvoke.Threshold(resultImage, resultImage, threshold, 255, Emgu.CV.CvEnum.ThresholdType.Binary);

            // Filter kleine vlekjes eruit met morphology
            System.Drawing.Point anchor = new System.Drawing.Point(-1, -1);
            Mat kernel = CvInvoke.GetStructuringElement(
                Emgu.CV.CvEnum.ElementShape.Rectangle,
                new System.Drawing.Size(10, 10),
                anchor // Kernel-anker, (-1, -1) is het centrum
            );
            CvInvoke.MorphologyEx(
                resultImage,
                resultImage,
                Emgu.CV.CvEnum.MorphOp.Open,
                kernel,
                anchor,
                1, // Aantal iteraties
                Emgu.CV.CvEnum.BorderType.Default,
                new MCvScalar(0) 
            );

            // Vind contouren in image
            Emgu.CV.Util.VectorOfVectorOfPoint contours = new Emgu.CV.Util.VectorOfVectorOfPoint{};
            Mat hierarchy = new Mat{};
            CvInvoke.FindContours(
                resultImage, 
                contours, 
                hierarchy, 
                Emgu.CV.CvEnum.RetrType.External, 
                Emgu.CV.CvEnum.ChainApproxMethod.ChainApproxSimple
            );

            System.Drawing.Point[][] contours_array = contours.ToArrayOfArray();
            List<System.Drawing.Point> center_positions = new List<System.Drawing.Point>();

            // Fit ellips om de centers te vinden
            for (int i = 0; i < contours_array.Length; i++) {
                if (contours_array[i].Length < 5) {
                    continue;
                }

                RotatedRect ellipse = CvInvoke.FitEllipse(contours[i]);
                System.Drawing.Point center =  System.Drawing.Point.Round(ellipse.Center);

                center_positions.Add(center);

                Visualize(image, ellipse, center);
            }

            // Console.WriteLine(center_positions.Count);

            // Check amount of fingers detected, if 1 -> save previous position
            if (center_positions.Count == 1) {
                if (previous_position != System.Drawing.Point.Empty && frame_count > click_frame_count_threshold) {
                    MoveMouse(center_positions[0]);
                }

                previous_position = center_positions[0];
                frame_count++;
            }
            else {
                if (previous_position != System.Drawing.Point.Empty && frame_count < click_frame_count_threshold) {
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                    // Console.WriteLine("click");
                }
                previous_position = System.Drawing.Point.Empty;
                frame_count = 0;
            }

            return image;
        }

        private void Visualize(Mat image, RotatedRect ellipse, System.Drawing.Point center)
        {
            // Draw the ellipse and center on the image
            CvInvoke.Ellipse(image, ellipse, new MCvScalar(255, 0, 0), 2); // Blue ellipse
            CvInvoke.Circle(image, center, 5, new MCvScalar(255, 0, 0), -1); // Blue center

            string textX = $"X: {center.X:F0}";
            System.Drawing.Point centerX = center;
            centerX.Y -= 10;

            string textY = $"Y: {center.Y:F0}";
            System.Drawing.Point centerY = center;
            centerY.Y += 10;

            CvInvoke.PutText(image, textX, centerX, Emgu.CV.CvEnum.FontFace.HersheySimplex, 0.5, new MCvScalar(0, 0, 255), 2); // Red text
            CvInvoke.PutText(image, textY, centerY, Emgu.CV.CvEnum.FontFace.HersheySimplex, 0.5, new MCvScalar(0, 0, 255), 2); // Red text
        }

        private void MoveMouse(System.Drawing.Point position)
        {
            double scale = 1080/640;

            System.Drawing.Point mouse_position = GetCursorPosition();

            int new_X = mouse_position.X + (int)(scale * (position.X - previous_position.X));
            int new_Y = mouse_position.Y + (int)(scale * (position.Y - previous_position.Y));
            
            SetCursorPos(new_X, new_Y);
        }

        // Camera correct te stoppen bij het sluiten van het programma:
        protected override void OnClosed(EventArgs e)
        {
            _capture.Stop();
            _capture.Dispose();
            base.OnClosed(e);
        }
    }
}
