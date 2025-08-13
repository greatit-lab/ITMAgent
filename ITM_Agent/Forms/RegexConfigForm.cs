// ITM_Agent/Forms/RegexConfigForm.cs
using ITM_Agent.Properties;
using System;
using System.Windows.Forms;

namespace ITM_Agent.Forms
{
    /// <summary>
    /// 정규표현식 패턴과 대상 폴더를 설정하기 위한 폼 클래스입니다.
    /// </summary>
    public partial class RegexConfigForm : Form
    {
        private readonly string _baseFolderPath;

        /// <summary>
        /// 사용자가 입력한 정규표현식 패턴입니다.
        /// </summary>
        public string RegexPattern
        {
            get => tb_RegInput.Text;
            set => tb_RegInput.Text = value;
        }

        /// <summary>
        /// 사용자가 선택한 대상 폴더 경로입니다.
        /// </summary>
        public string TargetFolder
        {
            get => tb_RegFolder.Text;
            set => tb_RegFolder.Text = value;
        }

        public RegexConfigForm(string baseFolderPath)
        {
            _baseFolderPath = baseFolderPath ?? AppDomain.CurrentDomain.BaseDirectory;
            InitializeComponent();
            InitializeFormProperties();
            RegisterEventHandlers();
        }

        /// <summary>
        /// 폼의 기본 속성을 설정합니다.
        /// </summary>
        private void InitializeFormProperties()
        {
            this.Text = "Regex Configuration";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            tb_RegFolder.ReadOnly = true; // 폴더 경로는 'Select' 버튼으로만 입력
        }

        /// <summary>
        /// 컨트롤의 이벤트 핸들러를 등록합니다.
        /// </summary>
        private void RegisterEventHandlers()
        {
            btn_RegSelectFolder.Click += Btn_RegSelectFolder_Click;
            btn_RegApply.Click += Btn_RegApply_Click;
            btn_RegCancel.Click += (sender, e) => this.DialogResult = DialogResult.Cancel;
        }

        private void Btn_RegSelectFolder_Click(object sender, EventArgs e)
        {
            using (var folderDialog = new FolderBrowserDialog())
            {
                // 대화상자의 초기 경로를 BaseFolder로 설정
                folderDialog.SelectedPath = System.IO.Directory.Exists(TargetFolder) ? TargetFolder : _baseFolderPath;

                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    TargetFolder = folderDialog.SelectedPath;
                }
            }
        }

        private void Btn_RegApply_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(RegexPattern))
            {
                // Resources.resx 파일에서 경고 메시지 로드
                MessageBox.Show(Resources.MSG_REGEX_REQUIRED,
                                Resources.CAPTION_WARNING,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(TargetFolder))
            {
                MessageBox.Show(Resources.MSG_FOLDER_REQUIRED,
                                Resources.CAPTION_WARNING,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        // RegexConfigForm.Designer.cs 파일은 UI 컨트롤의 자동 생성 코드를 포함하므로
        // 여기서는 생략합니다. (기존 파일 그대로 사용)
    }
}