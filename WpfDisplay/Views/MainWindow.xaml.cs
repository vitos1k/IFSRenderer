﻿using IFSEngine.Rendering;
using IFSEngine.Serialization;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using WpfDisplay.Models;
using WpfDisplay.ViewModels;

namespace WpfDisplay.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private EditorWindow editorWindow;
        private GeneratorWindow generatorWindow;
        private MainViewModel vm => (MainViewModel)DataContext;

        public MainWindow()
        {
            InitializeComponent();
            ContentRendered += (s, e) =>
            {
                //init workspace, tie renderer to display
                RendererGL renderer = new(mainDisplay.GraphicsContext);
                mainDisplay.AttachRenderer(renderer);
                Workspace workspace = new(renderer);
                if (App.OpenVerbPath is not null)
                {//handle open verb
                    workspace.LoadParams(IfsSerializer.LoadJsonFile(App.OpenVerbPath, workspace.LoadedTransforms, true));
                }
                DataContext = new MainViewModel(workspace);
            };
        }

        private void GeneratorButton_Click(object sender, RoutedEventArgs e)
        {
            //create window
            if (generatorWindow == null || !generatorWindow.IsLoaded)
            {
                generatorWindow = new GeneratorWindow();
                generatorWindow.DataContext = new GeneratorViewModel(vm);
            }

            if (generatorWindow.ShowActivated)
                generatorWindow.Show();
            //bring to foreground
            if (!generatorWindow.IsActive)
                generatorWindow.Activate();

            generatorWindow.WindowState = WindowState.Normal;
        }

        private void EditorButton_Click(object sender, RoutedEventArgs e)
        {
            //create window
            if (editorWindow == null || !editorWindow.IsLoaded)
            {
                editorWindow = new EditorWindow();
                editorWindow.SetBinding(DataContextProperty, new Binding(".") { Source = vm.IFSViewModel, Mode = BindingMode.TwoWay });
            }

            if (editorWindow.ShowActivated)
                editorWindow.Show();
            //bring to foreground
            if (!editorWindow.IsActive)
                editorWindow.Activate();

            editorWindow.WindowState = WindowState.Normal;
        }
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow
            {
                DataContext = new SettingsViewModel(vm)
            };
            if (settingsWindow.ShowDialog() == true)
                vm.StatusBarText = "Settings saved.";
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (generatorWindow != null)
                generatorWindow.Close();
            if (editorWindow != null)
                editorWindow.Close();
            vm.Dispose();
            base.OnClosing(e);
        }

        private void Undo_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            vm.IFSViewModel.UndoCommand.Execute(null);
        }

        private void Undo_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = vm?.IFSViewModel.UndoCommand.CanExecute(null) ?? false;
        }

        private void Redo_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            vm.IFSViewModel.RedoCommand.Execute(null);
        }

        private void Redo_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = vm?.IFSViewModel.RedoCommand.CanExecute(null) ?? false;
        }

    }
}
