// ITM_Agent/Forms/EqpidInputForm.cs
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ITM_Agent.Forms
{
    /// <summary>
    /// 신규 Eqpid 및 장비 타입을 사용자로부터 입력받기 위한 폼입니다.
    /// </summary>
    public class EqpidInputForm : Form
    {
        #region --- Public Properties ---

        /// <summary>
        /// 사용자가 입력한 Eqpid 값입니다.
        /// </summary>
        public string Eqpid { get; private set; }

        /// <summary>
        /// 사용자가 선택한 장비 타입("ONTO" 또는 "NOVA")입니다.
        /// </summary>
        public string Type { get; private set; }

        #endregion

        #region --- UI Controls ---

        private TextBox _textBoxEqpid;
        private Button _btnSubmit;
        private Button _btnCancel;
        private Label _lblInstruction;
        private Label _lblWarning;
        private PictureBox _picIcon;
        private RadioButton _rdoOnto;
        private RadioButton _rdoNova;

        #endregion

        public EqpidInputForm()
        {
            InitializeComponent();
            InitializeCustomLayout();
            RegisterEventHandlers();
        }

        /// <summary>
        /// 폼의 컨트롤들을 프로그래밍 방식으로 초기화하고 배치합니다.
        /// </summary>
        private void InitializeComponent()
        {
            // --- Form Properties ---
            this.Text = "New EQPID Registry";
            this.Size = new Size(300, 200);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.ControlBox = false; // 닫기 버튼 비활성화

            // --- Controls Initialization ---
            _lblInstruction = new Label();
            _textBoxEqpid = new TextBox();
            _lblWarning = new Label();
            _btnSubmit = new Button();
            _btnCancel = new Button();
            _picIcon = new PictureBox();
            _rdoOnto = new RadioButton();
            _rdoNova = new RadioButton();

            this.SuspendLayout();

            // --- Control Properties & Layout ---
            _lblInstruction.Text = "신규로 등록 필요한 장비명을 입력하세요.";
            _lblInstruction.Location = new Point(25, 20);
            _lblInstruction.AutoSize = true;

            _rdoOnto.Text = "ONTO";
            _rdoOnto.Location = new Point(115, 45);
            _rdoOnto.AutoSize = true;
            _rdoOnto.Checked = true; // 기본값

            _rdoNova.Text = "NOVA";
            _rdoNova.Location = new Point(_rdoOnto.Left + _rdoOnto.Width + 10, 45);
            _rdoNova.AutoSize = true;

            _textBoxEqpid.Location = new Point(115, 70);
            _textBoxEqpid.Size = new Size(150, 21);

            _lblWarning.Text = "장비명을 입력해주세요.";
            _lblWarning.Location = new Point(115, 95);
            _lblWarning.ForeColor = Color.Red;
            _lblWarning.AutoSize = true;
            _lblWarning.Visible = false;

            _btnSubmit.Text = "Submit";
            _btnSubmit.Location = new Point(50, 120);
            _btnSubmit.Size = new Size(90, 23);

            _btnCancel.Text = "Cancel";
            _btnCancel.Location = new Point(150, 120);
            _btnCancel.Size = new Size(90, 23);

            _picIcon.Size = new Size(75, 75);
            _picIcon.Location = new Point(22, 36);
            _picIcon.SizeMode = PictureBoxSizeMode.StretchImage;

            // --- Add Controls to Form ---
            this.Controls.Add(_lblInstruction);
            this.Controls.Add(_rdoOnto);
            this.Controls.Add(_rdoNova);
            this.Controls.Add(_textBoxEqpid);
            this.Controls.Add(_lblWarning);
            this.Controls.Add(_btnSubmit);
            this.Controls.Add(_btnCancel);
            this.Controls.Add(_picIcon);

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        /// <summary>
        /// 컨트롤의 Z-order(표시 순서) 조정 및 아이콘 이미지를 로드합니다.
        /// </summary>
        private void InitializeCustomLayout()
        {
            _picIcon.Image = CreateTransparentImage("Resources\\Icons\\icon.png", 128);
            _picIcon.SendToBack(); // 아이콘을 가장 뒤로 보냅니다.
        }

        /// <summary>
        /// 컨트롤의 이벤트 핸들러를 등록합니다.
        /// </summary>
        private void RegisterEventHandlers()
        {
            _btnSubmit.Click += BtnSubmit_Click;
            _btnCancel.Click += (sender, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            _textBoxEqpid.KeyDown += TextBoxEqpid_KeyDown;
        }

        private void BtnSubmit_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_textBoxEqpid.Text))
            {
                _lblWarning.Visible = true;
                return;
            }

            this.Eqpid = _textBoxEqpid.Text.Trim();
            this.Type = _rdoOnto.Checked ? "ONTO" : "NOVA";
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void TextBoxEqpid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true; // Enter 키 입력 시 '띵' 소리 제거
                _btnSubmit.PerformClick();   // Submit 버튼 클릭 이벤트 강제 발생
            }
        }

        /// <summary>
        /// 지정된 이미지 파일에 투명도(알파) 값을 적용하여 새 이미지를 생성합니다.
        /// </summary>
        private Image CreateTransparentImage(string filePath, int alpha)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                using (var original = new Bitmap(filePath))
                {
                    var transparentImage = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(transparentImage))
                    {
                        var colorMatrix = new ColorMatrix { Matrix33 = alpha / 255f };
                        var attributes = new ImageAttributes();
                        attributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

                        g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
                                    0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
                    }
                    return transparentImage;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EqpidInputForm] Could not create transparent image: {ex.Message}");
                return null;
            }
        }
    }
}
