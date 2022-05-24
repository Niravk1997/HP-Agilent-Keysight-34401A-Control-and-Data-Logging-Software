using HP_34401A_User_Controls;
using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HP_34401A
{
    public partial class Control_Window : MetroWindow
    {
        Measurement_Config Measurement_Config_Menu = new Measurement_Config();

        private void Set_Option_Menu_Selection(int Selection) 
        {
            switch (Selection) 
            {
                case 0:
                    Load_Option_Menu_Selection.Content = Measurement_Config_Menu;
                    break;
                default:
                    break;
            }
        }
    }
}
