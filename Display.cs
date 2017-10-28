using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UDPOscilloscope
{
    public partial class Display : Form
    {
        private readonly Timer t = new Timer { Interval = 20 };
        private Action act;


        public Display(Action ontick)
        {
            InitializeComponent();

            act = ontick;
            Load += Display_Load;
        }

        private void Display_Load(object sender, EventArgs e)
        {
            t.Tick += (s, a) => Invoke(new MethodInvoker(act));
            t.Start();
        }
    }
}
