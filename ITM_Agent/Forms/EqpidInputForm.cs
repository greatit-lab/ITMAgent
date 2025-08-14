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
            this._lblInstruction = new System.Windows.Forms.Label();
            this._textBoxEqpid = new System.Windows.Forms.TextBox();
            this._lblWarning = new System.Windows.Forms.Label();
            this._btnSubmit = new System.Windows.Forms.Button();
            this._btnCancel = new System.Windows.Forms.Button();
            this._picIcon = new System.Windows.Forms.PictureBox();
            this._rdoOnto = new System.Windows.Forms.RadioButton();
            this._rdoNova = new System.Windows.Forms.RadioButton();
            ((System.ComponentModel.ISupportInitialize)(this._picIcon)).BeginInit();

            this.SuspendLayout();
            //
            // _lblInstruction
            //
            this._lblInstruction.AutoSize = true;
            this._lblInstruction.Location = new System.Drawing.Point(25, 20);
            this._lblInstruction.Name = "_lblInstruction";
            this._lblInstruction.Size = new System.Drawing.Size(229, 12);
            this._lblInstruction.TabIndex = 0;
            this._lblInstruction.Text = "신규로 등록 필요한 장비명을 입력하세요.";
            //
            // _textBoxEqpid
            //
            this._textBoxEqpid.Location = new System.Drawing.Point(115, 70);
            this._textBoxEqpid.Name = "_textBoxEqpid";
            this._textBoxEqpid.Size = new System.Drawing.Size(150, 21);
            this._textBoxEqpid.TabIndex = 3;
            //
            // _textBoxEqpid
            //
            this._lblWarning.AutoSize = true;
            this._lblWarning.ForeColor = System.Drawing.Color.Red;
            this._lblWarning.Location = new System.Drawing.Point(115, 95);
            this._lblWarning.Name = "_lblWarning";
            this._lblWarning.Size = new System.Drawing.Size(133, 12);
            this._lblWarning.TabIndex = 4;
            this._lblWarning.Text = "장비명을 입력해주세요.";
            this._lblWarning.Visible = false;
            //
            // _btnSubmit
            //
            this._btnSubmit.Location = new System.Drawing.Point(50, 120);
            this._btnSubmit.Name = "_btnSubmit";
            this._btnSubmit.Size = new System.Drawing.Size(90, 23);
            this._btnSubmit.TabIndex = 5;
            this._btnSubmit.Text = "Submit";
            //
            // _btnCancel
            //
            this._btnCancel.Location = new System.Drawing.Point(150, 120);
            this._btnCancel.Name = "_btnCancel";
            this._btnCancel.Size = new System.Drawing.Size(90, 23);
            this._btnCancel.TabIndex = 6;
            this._btnCancel.Text = "Cancel";
            //
            // _picIcon
            //
            this._picIcon.Location = new System.Drawing.Point(22, 36);
            this._picIcon.Name = "_picIcon";
            this._picIcon.Size = new System.Drawing.Size(75, 75);
            this._picIcon.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this._picIcon.TabIndex = 7;
            this._picIcon.TabStop = false;
            //
            // _rdoOnto
            //
            this._rdoOnto.AutoSize = true;
            this._rdoOnto.Checked = true;
            this._rdoOnto.Location = new System.Drawing.Point(115, 45);
            this._rdoOnto.Name = "_rdoOnto";
            this._rdoOnto.Size = new System.Drawing.Size(58, 16);
            this._rdoOnto.TabIndex = 1;
            this._rdoOnto.TabStop = true;
            this._rdoOnto.Text = "ONTO";
            //
            // _rdoOnto
            //
            this._rdoNova.AutoSize = true;
            this._rdoNova.Location = new System.Drawing.Point(197, 45);
            this._rdoNova.Name = "_rdoNova";
            this._rdoNova.Size = new System.Drawing.Size(57, 16);
            this._rdoNova.TabIndex = 2;
            this._rdoNova.Text = "NOVA";
            //
            // EqpidInputForm
            //
            this.ClientSize = new System.Drawing.Size(284, 161);
            this.ControlBox = false;
            this.Controls.Add(_lblInstruction);
            this.Controls.Add(_rdoOnto);
            this.Controls.Add(_rdoNova);
            this.Controls.Add(_textBoxEqpid);
            this.Controls.Add(_lblWarning);
            this.Controls.Add(_btnSubmit);
            this.Controls.Add(_btnCancel);
            this.Controls.Add(_picIcon);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.Name = "EqpidInputForm";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "New EQPID Registry";
            ((System.ComponentModel.ISupportInitialize)(this._picIcon)).EndInit();
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
