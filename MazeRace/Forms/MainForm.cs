using System;
using System.Drawing;
using System.Windows.Forms;

namespace MazeRace
{
    public class MainForm : Form
    {
        private TextBox txtName;
        private NumericUpDown numDifficulty;
        private Button btnStart;
        private Label lblInfo;
        private Label lblAuthor;

        public MainForm()
        {
            Text = "迷宫竞速 - 单机版";
            ClientSize = new Size(600, 440);                    
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Icon = Properties.Resources.favicon; 

            Label lblName = new Label
            {
                Text = "玩家昵称:",
                Location = new Point(40, 50),                 
                Size = new Size(160, 40),                      
                Font = new Font("微软雅黑", 10)                 
            };
            txtName = new TextBox
            {
                Location = new Point(200, 44),                 
                Size = new Size(300, 40),                     
                Text = "",
                Font = new Font("微软雅黑", 10)
            };

            Label lblDiff = new Label
            {
                Text = "难度（1-100）:",
                Location = new Point(40, 120),                
                Size = new Size(160, 40),
                Font = new Font("微软雅黑", 10)
            };
            numDifficulty = new NumericUpDown
            {
                Location = new Point(200, 114),               
                Size = new Size(160, 40),                      
                Minimum = 1,
                Maximum = 100,
                Value = 50,
                TextAlign = HorizontalAlignment.Left,
                Font = new Font("Arial", 10)
            };

            btnStart = new Button
            {
                Text = "开始游戏",
                Location = new Point(140, 200),                
                Size = new Size(300, 80),                      
                Font = new Font("微软雅黑", 10)
            };
            btnStart.Click += BtnStart_Click;

            lblInfo = new Label
            {
                Text = "难度越高，分支越少，路径越曲折",
                Location = new Point(40, 300),                
                Size = new Size(520, 40),                      
                ForeColor = Color.DarkGreen,
                Font = new Font("微软雅黑", 10)
            };

            lblAuthor = new Label
            {
                Text = "作者: YuMoo\n版本：v1.1.0",
                Location = new Point(40, 360),                
                Size = new Size(520, 40),                      
                ForeColor = Color.Gray,
                Font = new Font("微软雅黑", 7),
                AutoSize = true
            };

            Controls.AddRange(new Control[] { lblName, txtName, lblDiff, numDifficulty, btnStart, lblInfo, lblAuthor });
        }

        private void BtnStart_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                MessageBox.Show("请输入昵称");
                return;
            }
            int difficulty = (int)numDifficulty.Value;
            var gameForm = new GameForm(txtName.Text, difficulty);
            gameForm.FormClosed += (s, args) => this.Show();
            Hide();
            gameForm.Show();
        }
    }
}