// ITM_Agent/Forms/AboutInfoForm.cs
using ITM_Agent.Properties;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ITM_Agent.Forms
{
    /// <summary>
    /// 애플리케이션 정보를 표시하는 폼 클래스입니다.
    /// </summary>
    public partial class AboutInfoForm : Form
    {
        public AboutInfoForm()
        {
            InitializeComponent();
            LoadAndDisplayInfo();
        }

        /// <summary>
        /// 폼에 필요한 정보(버전, 설명, 아이콘 등)를 로드하고 표시합니다.
        /// </summary>
        private void LoadAndDisplayInfo()
        {
            // MainForm으로부터 정적 속성을 통해 버전 정보 가져오기
            lb_Version.Text = MainForm.VersionInfo;

            // Resources.resx 파일에서 다국어 텍스트 불러와 UI에 적용
            this.label1.Text = Resources.AboutInfo_Desc1;
            this.label2.Text = Resources.AboutInfo_Desc2;
            this.label3.Text = Resources.AboutInfo_Desc3;
            this.label4.Text = Resources.AboutInfo_Desc4;

            // 아이콘 파일을 안전하게 로드하여 투명도 적용 후 표시
            LoadIconSafe();
        }

        /// <summary>
        /// 아이콘 파일을 안전하게 로드하고, 실패 시 기본 아이콘을 사용합니다.
        /// </summary>
        private void LoadIconSafe()
        {
            try
            {
                string iconPath = Path.Combine(Application.StartupPath, "Resources", "Icons", "icon.png");

                Image baseImage = File.Exists(iconPath)
                                ? Image.FromFile(iconPath)
                                : SystemIcons.Application.ToBitmap();

                // 아이콘에 50% 투명도 적용
                picIcon.Image = ApplyOpacity(baseImage, 0.5f);
            }
            catch (Exception ex)
            {
                // 예기치 않은 오류 발생 시 시스템 기본 아이콘으로 대체
                // 이 폼은 LogManager를 주입받지 않으므로, Console로 간단히 기록합니다.
                Console.WriteLine($"[ERROR] AboutInfoForm - Failed to load icon: {ex.Message}");
                picIcon.Image = SystemIcons.Application.ToBitmap();
            }
        }

        /// <summary>
        /// 주어진 이미지에 지정된 투명도를 적용한 새 비트맵 이미지를 생성합니다.
        /// </summary>
        /// <param name="sourceImage">원본 이미지입니다.</param>
        /// <param name="opacity">적용할 불투명도 값입니다. (0.0f: 완전 투명, 1.0f: 완전 불투명)</param>
        /// <returns>투명도가 적용된 32bpp ARGB 형식의 새 비트맵입니다.</returns>
        private static Bitmap ApplyOpacity(Image sourceImage, float opacity)
        {
            // 32비트 ARGB 포맷으로 새 Bitmap을 생성해야 알파 채널(투명도)을 지원
            var bmp = new Bitmap(sourceImage.Width, sourceImage.Height, PixelFormat.Format32bppArgb);

            using (Graphics g = Graphics.FromImage(bmp))
            {
                var colorMatrix = new ColorMatrix
                {
                    Matrix33 = opacity // Alpha 채널(투명도) 값 설정
                };

                var imageAttributes = new ImageAttributes();
                imageAttributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

                g.DrawImage(sourceImage,
                    new Rectangle(0, 0, bmp.Width, bmp.Height),
                    0, 0, sourceImage.Width, sourceImage.Height,
                    GraphicsUnit.Pixel, imageAttributes);
            }
            return bmp;
        }

        // AboutInfoForm.Designer.cs 파일은 UI 컨트롤의 자동 생성 코드를 포함하므로
        // 여기서는 생략합니다. (기존 파일 그대로 사용)
    }
}
