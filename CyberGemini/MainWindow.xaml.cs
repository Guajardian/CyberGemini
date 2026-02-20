using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CyberGemini.ViewModels;

namespace CyberGemini;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_BORDER_COLOR = 34;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            var hwnd = source.Handle;

            // Enable dark title bar
            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

            // Set caption color to match background (#0A0E17 = RGB 10, 14, 23 → COLORREF 0x00170E0A)
            int captionColor = 0x00170E0A;
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

            // Set border color to match surface border (#2A3A50 = RGB 42, 58, 80 → COLORREF 0x00503A2A)
            int borderColor = 0x00503A2A;
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
        }
    }
}
