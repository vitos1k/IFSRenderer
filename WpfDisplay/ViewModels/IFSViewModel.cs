﻿using IFSEngine.Model;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Microsoft.Toolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using WpfDisplay.Helper;
using WpfDisplay.Models;

namespace WpfDisplay.ViewModels
{
    public class IFSViewModel : ObservableObject
    {
        private readonly Workspace workspace;

        public IReadOnlyCollection<TransformFunction> RegisteredTransforms => workspace.LoadedTransforms;
        public ObservableCollection<IteratorViewModel> IteratorViewModels { get; private set; } = new ObservableCollection<IteratorViewModel>();


        private IteratorViewModel connectingIterator;

        private IteratorViewModel selectedIterator;
        public IteratorViewModel SelectedIterator
        {
            get => selectedIterator; 
            set
            {
                if(selectedIterator != null)
                    selectedIterator.IsSelected = false;

                SetProperty(ref selectedIterator, value);

                if (selectedIterator != null)
                {
                    SelectedConnection = null;
                    selectedIterator.IsSelected = true;
                }
            }
        }

        public Visibility IsIteratorEditorVisible => SelectedIterator == null ? Visibility.Collapsed : Visibility.Visible;

        private ConnectionViewModel selectedConnection;
        public ConnectionViewModel SelectedConnection
        {
            get => selectedConnection;
            set
            {
                if (selectedConnection != null)
                    selectedConnection.IsSelected = false;

                SetProperty(ref selectedConnection, value);
                OnPropertyChanged(nameof(IsConnectionEditorVisible));

                if (selectedConnection != null)
                {
                    SelectedIterator = null;
                    selectedConnection.IsSelected = true;
                }
            }
        }

        public Visibility IsConnectionEditorVisible => SelectedConnection == null ? Visibility.Collapsed : Visibility.Visible;

        public Color BackgroundColor
        {
            get 
            {
                var c = workspace.IFS.BackgroundColor;
                return Color.FromRgb(c.R, c.G, c.B);
            }
            set
            {
                workspace.IFS.BackgroundColor = System.Drawing.Color.FromArgb(255, value.R, value.G, value.B);
                workspace.Renderer.InvalidateDisplay();
                OnPropertyChanged(nameof(BackgroundColor));
            }
        }

        public FlamePalette Palette => workspace.IFS.Palette;

        public double FogEffect
        {
            get => workspace.IFS.FogEffect;
            set
            {
                workspace.IFS.FogEffect = value;
                workspace.Renderer.InvalidateHistogramBuffer();
                OnPropertyChanged(nameof(FogEffect));
            }
        }

        public IFSViewModel(Workspace workspace)
        {
            this.workspace = workspace;
            workspace.PropertyChanged += (s, e) => OnPropertyChanged(string.Empty);
            IteratorViewModels.CollectionChanged += (s, e) => workspace.Renderer.InvalidateParamsBuffer();
            workspace.IFS.Iterators.ToList().ForEach(i => AddNewIteratorVM(i));
            workspace.PropertyChanged += (s, e) => {
                SelectedIterator = null;
                HandleIteratorsChanged(); 
            };
            HandleIteratorsChanged();
        }

        private IteratorViewModel AddNewIteratorVM(Iterator i)
        {
            var ivm = new IteratorViewModel(i, workspace)
            {
                RemoveCommand = RemoveSelectedCommand,
                DuplicateCommand = DuplicateSelectedCommand
            };
            ivm.PropertyChanged += (s, e) => OnPropertyChanged(e.PropertyName);
            ivm.ViewChanged += (s, e) => { Redraw(); };
            ivm.ConnectEvent += (s, finish) =>
            {
                if (!finish)
                    connectingIterator = ivm;
                else if(connectingIterator != null)
                {
                    workspace.TakeSnapshot();
                    if (connectingIterator.iterator.WeightTo.ContainsKey(ivm.iterator))
                        connectingIterator.iterator.WeightTo.Remove(ivm.iterator);
                    else
                        connectingIterator.iterator.WeightTo[ivm.iterator] = 1.0;
                    HandleConnectionsChanged(connectingIterator);
                    SelectedConnection = connectingIterator.ConnectionViewModels.FirstOrDefault(c=>c.to==ivm);
                    connectingIterator = null;

                }
            };
            if (SelectedIterator != null)
            {
                ivm.XCoord = SelectedIterator.XCoord + (float)SelectedIterator.WeightedSize / 1.5f + (float)ivm.WeightedSize / 1.5f;
                ivm.YCoord = SelectedIterator.YCoord;
            }
            IteratorViewModels.Add(ivm);
            SelectedIterator = ivm;
            return ivm;
        }

        private void HandleIteratorsChanged()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var newIterators = workspace.IFS.Iterators.Where(i => !IteratorViewModels.Any(vm => vm.iterator == i));
                var removedIteratorVMs = IteratorViewModels.Where(vm => !workspace.IFS.Iterators.Any(i => vm.iterator == i));
                removedIteratorVMs.ToList().ForEach(vm => IteratorViewModels.Remove(vm));
                newIterators.ToList().ForEach(i => AddNewIteratorVM(i));

                //update connections vms:
                IteratorViewModels.ToList().ForEach(vm => HandleConnectionsChanged(vm));
            });
        }
        private void HandleConnectionsChanged(IteratorViewModel vm)
        {
            var newConnections = vm.iterator.WeightTo.Where(w => !vm.ConnectionViewModels.Any(c => c.to.iterator == w.Key && w.Value > 0.0));
            var removedConnections = vm.ConnectionViewModels.Where(c => !vm.iterator.WeightTo.Any(i => c.to.iterator == i.Key && i.Value > 0.0));
            removedConnections.ToList().ForEach(vm2 => vm.ConnectionViewModels.Remove(vm2));
            newConnections.ToList().ForEach(c => vm.ConnectionViewModels.Add(new ConnectionViewModel(vm, IteratorViewModels.First(vm2=>vm2.iterator == c.Key), workspace)));
            Redraw();
        }

        public void Redraw()
        {
            foreach (var i in IteratorViewModels)
            {
                i.Redraw();
                foreach (var con in i.ConnectionViewModels)
                {
                    con.UpdateGeometry();
                }
            }
        }

        private RelayCommand<TransformFunction> _addIteratorCommand;
        public RelayCommand<TransformFunction> AddIteratorCommand => _addIteratorCommand
            ??= new((tf) =>
            {
                workspace.TakeSnapshot();
                Iterator newIterator = new(tf);
                workspace.IFS.AddIterator(newIterator, false);
                if (SelectedIterator != null)
                {
                    SelectedIterator.iterator.WeightTo[newIterator] = 1.0;
                    newIterator.WeightTo[SelectedIterator.iterator] = 1.0;
                }
                workspace.Renderer.InvalidateParamsBuffer();
                HandleIteratorsChanged();
            });

        private RelayCommand _removeSelectedCommand;
        public RelayCommand RemoveSelectedCommand => _removeSelectedCommand
            ??= new(() =>
            {
                workspace.TakeSnapshot();
                workspace.IFS.RemoveIterator(SelectedIterator.iterator);
                workspace.Renderer.InvalidateParamsBuffer();
                SelectedIterator = null;
                HandleIteratorsChanged();
            });

        private RelayCommand _duplicateSelectedCommand;
        public RelayCommand DuplicateSelectedCommand => _duplicateSelectedCommand
            ??= new(() =>
            {
                workspace.TakeSnapshot();
                Iterator dupe = workspace.IFS.DuplicateIterator(SelectedIterator.iterator);
                workspace.Renderer.InvalidateParamsBuffer();
                HandleIteratorsChanged();
                SelectedIterator = IteratorViewModels.First(vm => vm.iterator == dupe);
            });

        private AsyncRelayCommand _loadPaletteCommand;
        public AsyncRelayCommand LoadPaletteCommand => _loadPaletteCommand
            ??= new(async () =>
            {
                if (DialogHelper.ShowOpenPaletteDialog(out string path))
                {
                    var picker = new Views.PaletteDialogWindow
                    {
                        Palettes = await FlamePalette.FromFileAsync(path)
                    };
                    if (picker.ShowDialog() == true)
                    {
                        workspace.TakeSnapshot();
                        workspace.IFS.Palette = picker.SelectedPalette;
                        workspace.Renderer.InvalidateParamsBuffer();
                        OnPropertyChanged(nameof(Palette));
                    }
                }
            });

        private AsyncRelayCommand _reloadTransformsCommand;
        public AsyncRelayCommand ReloadTransformsCommand => _reloadTransformsCommand
            ??= new(async () =>
            {
                await workspace.ReloadTransforms();
                foreach (IteratorViewModel ivm in IteratorViewModels)
                    ivm.ReloadVariables();//handles when the number and names of variables have changed.
                OnPropertyChanged(nameof(RegisteredTransforms));
            });

        private RelayCommand _openTransformsDirectoryCommand;
        public RelayCommand OpenTransformsDirectoryCommand => _openTransformsDirectoryCommand
            ??= new(() =>
            {
                //show the directory with the os file explorer
                Process.Start(new ProcessStartInfo
                {
                    FileName = workspace.TransformsDirectoryPath,
                    UseShellExecute = true
                });
            });

        private RelayCommand<string> _editTransformSourceCommand;
        public RelayCommand<string> EditTransformSourceCommand => _editTransformSourceCommand
            ??= new((filePath) =>
            {
                //open transform source file with the preferred text editor
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            });

        private RelayCommand<ConnectionViewModel> _selectConnectionCommand;
        public RelayCommand<ConnectionViewModel> SelectConnectionCommand => _selectConnectionCommand
            ??= new((con) =>
            {
                SelectedConnection = con;
            });

        private RelayCommand<IteratorViewModel> _selectIteratorCommand;
        public RelayCommand<IteratorViewModel> SelectIteratorCommand => _selectIteratorCommand
            ??= new((it) =>
            {
                SelectedIterator = it;
            });

        private RelayCommand _undoCommand;
        public RelayCommand UndoCommand => _undoCommand
            ??= new(() =>
            {
                workspace.UndoHistory();
            }, () => workspace.IsHistoryUndoable);

        private RelayCommand _redoCommand;
        public RelayCommand RedoCommand => _redoCommand
            ??= new(() =>
            {
                workspace.RedoHistory();
            }, () => workspace.IsHistoryRedoable);

        private RelayCommand _takeSnapshotCommand;
        public RelayCommand TakeSnapshotCommand =>
            _takeSnapshotCommand ??= new RelayCommand(workspace.TakeSnapshot);

    }
}
