using HP_34401A.Misc;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace HP_34401A
{
    public partial class Control_Window : INotifyPropertyChanged
    {
        private ICommand Set_Home_Menu_;
        public ICommand Set_Home_Menu
        {
            get
            {
                if (Set_Home_Menu_ == null)
                {
                    Set_Home_Menu_ = new RelayCommand(p => true, p => Set_Option_Menu_Selection(0));
                }
                return Set_Home_Menu_;
            }
        }
    }
}
