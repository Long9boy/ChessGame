using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using ChessGame_PJ.Services;

namespace ChessGame_PJ
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            SoundService.Initialize();

            // Load pre-recorded voice clips into memory
            SpeechService.Initialize();


            // Register global handler for Button/Option clicks to play interact.ogg (excluding dialogs)
            EventManager.RegisterClassHandler(
                typeof(ButtonBase),
                ButtonBase.ClickEvent,
                new RoutedEventHandler(OnGlobalButtonClick),
                handledEventsToo: true
            );
        }

        private void OnGlobalButtonClick(object sender, RoutedEventArgs e)
        {
            // Ignore internal scrollbar and slider repeat buttons
            if (sender is RepeatButton) return;

            if (sender is not DependencyObject d) return;

            // Find parent window
            Window? win = Window.GetWindow(d);
            if (win != null)
            {
                string typeName = win.GetType().Name;
                if (typeName.Contains("Dialog", StringComparison.OrdinalIgnoreCase) ||
                    typeName.Contains("Promotion", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // Check if window is being shown as a modal dialog
                var isModalField = typeof(Window).GetField("_showingAsDialog", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (isModalField != null && (bool?)isModalField.GetValue(win) == true)
                {
                    return;
                }
            }

            SoundService.PlayInteract();
        }
    }
}