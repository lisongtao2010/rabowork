using System;
using System.Windows.Forms;

namespace CadPartSplitter
{
    /// <summary>
    /// 程序入口。
    ///
    /// 默认启动图形界面（选择 PDF / 图片后执行零件分割）；
    /// 若带命令行参数启动，则进入命令行模式（保留原有用法）。
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args != null && args.Length > 0)
                return CommandLineRunner.Run(args);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
    }
}
