using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using Emgu.CV;
using Emgu.CV.Structure;

namespace FTIR_Touchpad
{
    public partial class MainWindow : Window
    {
        private VideoCapture _capture;
        private Mat _frame;

        private Emgu.CV.UI.ImageBox imageBox;

        private double threshold;

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
                Value = 125, // Standaardwaarde
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

            System.Drawing.Point[][] test = contours.ToArrayOfArray();

            // Fit ellips om de centers te vinden
            for (int i = 0; i < test.Length; i++) {
                if (test[i].Length < 5) {
                    continue;
                }

                RotatedRect ellipse = CvInvoke.FitEllipse(contours[i]);
                System.Drawing.Point center =  System.Drawing.Point.Round(ellipse.Center);

                // Draw the ellipse and center on the image
                CvInvoke.Ellipse(image, ellipse, new MCvScalar(255, 0, 0), 2); // Blue ellipse
                CvInvoke.Circle(image, center, 5, new MCvScalar(255, 0, 0), -1); // Blue center

                string textX = $"X: {center.X:F2}";
                System.Drawing.Point centerX = center;
                centerX.Y -= 10;

                string textY = $"Y: {center.Y:F2}";
                System.Drawing.Point centerY = center;
                centerY.Y += 10;

                CvInvoke.PutText(image, textX, centerX, Emgu.CV.CvEnum.FontFace.HersheySimplex, 0.5, new MCvScalar(0, 0, 255), 2); // Red text
                CvInvoke.PutText(image, textY, centerY, Emgu.CV.CvEnum.FontFace.HersheySimplex, 0.5, new MCvScalar(0, 0, 255), 2); // Red text
            }

            return image;
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
