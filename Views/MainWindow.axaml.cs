using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using VoiceRec.ViewModels;

namespace VoiceRec.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;
    private bool _isFocused;

    public MainWindow()
    {
        InitializeComponent();
        
        // Enable transparency - use array for compatibility
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        
        // Setup focus events
        PointerPressed += OnPointerPressed;
        KeyDown += OnKeyDown;
        
        // Setup window focus tracking
        Activated += OnWindowActivated;
        Deactivated += OnWindowDeactivated;
        
        Opacity = 1;
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        
        if (DataContext is MainViewModel vm)
        {
            _viewModel = vm;
        }
    }

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        
        // Initialize ViewModel asynchronously
        if (_viewModel != null)
        {
            _ = _viewModel.InitializeAsync();
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Focus window on click
        if (!IsFocused)
        {
            Focus();
        }
    }

    private void OnBorderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Enable window dragging from the border
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Handle keyboard shortcuts
        if (e.Key == Key.Escape)
        {
            // Minimize on Escape
            WindowState = WindowState.Minimized;
        }
    }

    private void OnWindowActivated(object? sender, System.EventArgs e)
    {
        _isFocused = true;
        
        // Restore opacity when focused
        Dispatcher.UIThread.Post(() => Opacity = 1.0);
    }

    private void OnWindowDeactivated(object? sender, System.EventArgs e)
    {
        _isFocused = false;
        
        // Make transparent when not focused
        Dispatcher.UIThread.Post(() => Opacity = 0.85);
    }

    private void OnCloseButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Clean up resources
        _viewModel?.Dispose();
        Close();
    }
}
