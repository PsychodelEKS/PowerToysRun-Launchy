using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;
using Community.PowerToys.Run.Plugin.Launchy.Models;
using Forms = System.Windows.Forms;
using Wpf = System.Windows.Controls;

namespace Community.PowerToys.Run.Plugin.Launchy.UI;

public sealed class SettingsPanel : Wpf.UserControl
{
    private readonly ObservableCollection<LaunchyFolderRule> _rules;
    private readonly Wpf.CheckBox _enableGlobalResults;
    private readonly Wpf.DataGrid _grid;
    private readonly Action<LaunchySettings> _saveSettings;
    private readonly Func<Task<int?>> _rescanAsync;

    public SettingsPanel(
        LaunchySettings settings,
        Action<LaunchySettings> saveSettings,
        Func<Task<int?>> rescanAsync)
    {
        _rules = new ObservableCollection<LaunchyFolderRule>(settings.FolderRules.Select(rule => rule.Clone()));
        _saveSettings = saveSettings;
        _rescanAsync = rescanAsync;

        _enableGlobalResults = new Wpf.CheckBox
        {
            Content = "Show indexed items in global results",
            IsChecked = settings.EnableGlobalResults,
            Margin = new Thickness(0, 0, 0, 8),
        };

        _grid = CreateGrid();
        Content = CreateLayout();
    }

    private UIElement CreateLayout()
    {
        var root = new Wpf.DockPanel
        {
            LastChildFill = true,
            Margin = new Thickness(12),
        };

        Wpf.DockPanel.SetDock(_enableGlobalResults, Wpf.Dock.Top);
        root.Children.Add(_enableGlobalResults);

        var description = new Wpf.TextBlock
        {
            Text = "This editor updates the same Folder rules text shown in PowerToys Settings. Format: path | extensions | maxDepth | includeDirectories | enabled | matchDirectoryNames",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Wpf.DockPanel.SetDock(description, Wpf.Dock.Top);
        root.Children.Add(description);

        var buttons = new Wpf.StackPanel
        {
            Orientation = Wpf.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var addButton = new Wpf.Button { Content = "Add folder", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        addButton.Click += (_, _) => AddFolder();

        var removeButton = new Wpf.Button { Content = "Remove", MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        removeButton.Click += (_, _) => RemoveSelected();

        var saveButton = new Wpf.Button { Content = "Save", MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        saveButton.Click += (_, _) => Save();

        var rescanButton = new Wpf.Button { Content = "Save and rescan", MinWidth = 120 };
        rescanButton.Click += async (_, _) =>
        {
            Save();
            rescanButton.IsEnabled = false;
            try
            {
                await _rescanAsync().ConfigureAwait(true);
            }
            finally
            {
                rescanButton.IsEnabled = true;
            }
        };

        buttons.Children.Add(addButton);
        buttons.Children.Add(removeButton);
        buttons.Children.Add(saveButton);
        buttons.Children.Add(rescanButton);

        Wpf.DockPanel.SetDock(buttons, Wpf.Dock.Bottom);
        root.Children.Add(buttons);

        root.Children.Add(_grid);
        return root;
    }

    private Wpf.DataGrid CreateGrid()
    {
        var grid = new Wpf.DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = true,
            ItemsSource = _rules,
            MinHeight = 220,
        };

        grid.Columns.Add(new Wpf.DataGridCheckBoxColumn
        {
            Header = "Enabled",
            Binding = new System.Windows.Data.Binding(nameof(LaunchyFolderRule.Enabled)),
            Width = Wpf.DataGridLength.SizeToHeader,
        });

        grid.Columns.Add(new Wpf.DataGridTextColumn
        {
            Header = "Path",
            Binding = new System.Windows.Data.Binding(nameof(LaunchyFolderRule.Path)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new Wpf.DataGridLength(3, Wpf.DataGridLengthUnitType.Star),
        });

        grid.Columns.Add(new Wpf.DataGridTextColumn
        {
            Header = "Extensions",
            Binding = new System.Windows.Data.Binding(nameof(LaunchyFolderRule.Extensions)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new Wpf.DataGridLength(1.4, Wpf.DataGridLengthUnitType.Star),
        });

        grid.Columns.Add(new Wpf.DataGridTextColumn
        {
            Header = "Depth",
            Binding = new System.Windows.Data.Binding(nameof(LaunchyFolderRule.MaxDepth)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = Wpf.DataGridLength.SizeToHeader,
        });

        grid.Columns.Add(new Wpf.DataGridCheckBoxColumn
        {
            Header = "Include folders",
            Binding = new System.Windows.Data.Binding(nameof(LaunchyFolderRule.IncludeDirectories)),
            Width = Wpf.DataGridLength.SizeToHeader,
        });

        grid.Columns.Add(new Wpf.DataGridCheckBoxColumn
        {
            Header = "Folder name matches",
            Binding = new System.Windows.Data.Binding(nameof(LaunchyFolderRule.MatchDirectoryNames)),
            Width = Wpf.DataGridLength.SizeToHeader,
        });

        return grid;
    }

    private void AddFolder()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select a folder to index",
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        _rules.Add(new LaunchyFolderRule
        {
            Path = dialog.SelectedPath,
            Extensions = ".exe;.lnk",
            MaxDepth = 10,
            IncludeDirectories = false,
            MatchDirectoryNames = false,
            Enabled = true,
        });
    }

    private void RemoveSelected()
    {
        var selectedRules = _grid.SelectedItems.Cast<LaunchyFolderRule>().ToList();
        foreach (var selectedRule in selectedRules)
        {
            _rules.Remove(selectedRule);
        }
    }

    private void Save()
    {
        _grid.CommitEdit(Wpf.DataGridEditingUnit.Cell, true);
        _grid.CommitEdit(Wpf.DataGridEditingUnit.Row, true);

        _saveSettings(new LaunchySettings
        {
            EnableGlobalResults = _enableGlobalResults.IsChecked == true,
            FolderRules = _rules.Select(rule => rule.Clone()).ToList(),
        });
    }
}
