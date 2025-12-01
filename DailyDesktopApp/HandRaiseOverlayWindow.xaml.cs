using System.Windows;

namespace DailyDesktopApp
{
    public partial class HandRaiseOverlayWindow : Window
    {
        public HandRaiseOverlayWindow(string namesText, int count)
        {
            InitializeComponent();

            if (count <= 1)
            {
                DetailsText.Text = $"{namesText} has raised their hand.";
            }
            else
            {
                DetailsText.Text = $"{namesText} have raised their hands.";
            }
        }
    }
}
