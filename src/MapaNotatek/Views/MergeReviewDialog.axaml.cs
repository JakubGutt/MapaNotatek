using System.ComponentModel;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using MapaNotatek.Models;
using MapaNotatek.Services;

namespace MapaNotatek.Views;

public partial class MergeReviewDialog : Window
{
    private MergeProposal _proposal = null!;

    public MergeReviewDialog()
    {
        InitializeComponent();
    }

    public MergeReviewDialog(MergeProposal proposal) : this()
    {
        _proposal = proposal;
        TitleText.Text = proposal.IsFirstImport
            ? $"Nowy system: {proposal.Manifest.SourceSystemName}"
            : $"Aktualizacja: {proposal.Manifest.SourceSystemName}";
        MetadataText.Text =
            $"Źródło: {proposal.Manifest.SourceLibraryId:N}  •  pakiet: {proposal.Manifest.PackageId:N}  •  " +
            $"{proposal.Manifest.CreatedUtc.ToLocalTime():g}";
        ChangesList.ItemsSource = proposal.Changes;
        foreach (var change in proposal.Changes)
        {
            change.SelectionChanged += UpdateState;
            change.PropertyChanged += OnChangePropertyChanged;
            foreach (var conflict in change.Conflicts)
            {
                conflict.ResolutionChanged += UpdateState;
            }
        }

        if (proposal.Changes.Count > 0)
        {
            ChangesList.SelectedIndex = 0;
        }
        else
        {
            DetailsPanel.Children.Add(new TextBlock
            {
                Text = "Ta paczka nie zawiera nowych zmian względem ostatnio przejrzanej wersji.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.72
            });
        }
        WarningText.Text = string.Join("  ", proposal.Warnings);
        UpdateState();
    }

    public bool Accepted { get; private set; }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => RenderDetails();

    private void RenderDetails()
    {
        DetailsPanel.Children.Clear();
        if (ChangesList.SelectedItem is not MergeChange change)
        {
            return;
        }

        DetailsPanel.Children.Add(new TextBlock
        {
            Text = change.Title,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        DetailsPanel.Children.Add(new TextBlock
        {
            Text = $"{change.KindLabel} • {change.ChangeLabel}\n{change.Summary}",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72
        });

        if (change.Conflicts.Count == 0)
        {
            DetailsPanel.Children.Add(Section("PODGLĄD ZMIANY", DescribeChange(change)));
            return;
        }

        DetailsPanel.Children.Add(new TextBlock
        {
            Text = "KONFLIKTY",
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Opacity = 0.62
        });
        foreach (var conflict in change.Conflicts)
        {
            DetailsPanel.Children.Add(BuildConflict(conflict));
        }
    }

    private Control BuildConflict(MergeConflict conflict)
    {
        var choices = new List<ResolutionChoice>
        {
            new("Wybierz rozwiązanie…", MergeResolution.Unresolved),
            new("Zachowaj moją wersję", MergeResolution.Mine),
            new("Przyjmij wersję kolegi", MergeResolution.Theirs)
        };
        if (conflict.CanKeepBoth)
        {
            choices.Add(new ResolutionChoice("Zachowaj obie / połącz", MergeResolution.Both));
        }

        var selector = new ComboBox
        {
            ItemsSource = choices,
            SelectedItem = choices.FirstOrDefault(choice => choice.Value == conflict.Resolution) ?? choices[0],
            MinWidth = 230,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        selector.SelectionChanged += (_, _) =>
        {
            if (selector.SelectedItem is ResolutionChoice choice)
            {
                conflict.Resolution = choice.Value;
            }
        };

        var versions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 10
        };
        versions.Children.Add(VersionPanel("MOJA WERSJA", conflict.MineText, 0));
        versions.Children.Add(VersionPanel("WERSJA KOLEGI", conflict.TheirText, 1));
        return new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Gray,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = conflict.Label, FontWeight = FontWeight.SemiBold },
                    versions,
                    new TextBlock
                    {
                        Text = string.IsNullOrEmpty(conflict.BaseText) ? "Baza: (brak)" : "Baza:\n" + conflict.BaseText,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 11,
                        Opacity = 0.55,
                        TextWrapping = TextWrapping.Wrap,
                        MaxHeight = 110
                    },
                    selector
                }
            }
        };
    }

    private static Control VersionPanel(string title, string value, int column)
    {
        var panel = new Border
        {
            Padding = new Thickness(10),
            Background = new SolidColorBrush(Color.FromArgb(18, 100, 116, 139)),
            CornerRadius = new CornerRadius(6),
            Child = new StackPanel
            {
                Spacing = 5,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 10, FontWeight = FontWeight.SemiBold, Opacity = 0.62 },
                    new TextBlock
                    {
                        Text = string.IsNullOrEmpty(value) ? "(brak)" : value,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        MaxHeight = 190
                    }
                }
            }
        };
        Grid.SetColumn(panel, column);
        return panel;
    }

    private static Border Section(string title, string body) => new()
    {
        Padding = new Thickness(12),
        Background = new SolidColorBrush(Color.FromArgb(16, 100, 116, 139)),
        CornerRadius = new CornerRadius(8),
        Child = new StackPanel
        {
            Spacing = 7,
            Children =
            {
                new TextBlock { Text = title, FontSize = 10, FontWeight = FontWeight.SemiBold, Opacity = 0.62 },
                new TextBlock
                {
                    Text = body,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        }
    };

    private static string DescribeChange(MergeChange change)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Przed:");
        builder.AppendLine(Describe(change.MineEntity));
        builder.AppendLine();
        builder.AppendLine("Po zaakceptowaniu:");
        builder.AppendLine(Describe(change.TheirEntity));
        return builder.ToString().TrimEnd();
    }

    private static string Describe(object? value) => value switch
    {
        TransferProject project =>
            $"Nazwa: {project.Name}\nTyp: {project.ItemType.Label()}\nRodzic: {project.ParentId ?? "root"}\n" +
            $"Osoby: {string.Join(", ", project.People)}\n\n{project.Description}",
        TransferNote note =>
            $"Tytuł: {note.Title}\nTagi: {string.Join(", ", note.Tags)}\nOsoby: {string.Join(", ", note.People)}\n\n{note.Body}",
        TransferPerson person => $"Osoba: {person.Name}\nRola: {person.Role}\n\n{person.Description}",
        null => "(brak)",
        _ => value.ToString() ?? "(brak)"
    };

    private void UpdateState()
    {
        var errors = SystemMergeService.ValidateSelections(_proposal);
        ValidationText.Text = string.Join("  ", errors);
        MergeButton.IsEnabled = _proposal.SelectedCount > 0 && errors.Count == 0;
        var added = _proposal.Changes.Count(change => change.IsSelected && change.ChangeType == MergeChangeType.Added);
        var modified = _proposal.Changes.Count(change => change.IsSelected && change.ChangeType is MergeChangeType.Modified or MergeChangeType.Moved);
        var deleted = _proposal.Changes.Count(change => change.IsSelected && change.ChangeType == MergeChangeType.Deleted);
        SummaryText.Text = $"Wybrano {_proposal.SelectedCount}/{_proposal.Changes.Count}: +{added}  ~{modified}  −{deleted}";
    }

    private void OnChangePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MergeChange.IsSelected)) UpdateState();
    }

    private void OnMerge(object? sender, RoutedEventArgs e)
    {
        if (!MergeButton.IsEnabled) return;
        Accepted = true;
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private sealed record ResolutionChoice(string Label, MergeResolution Value)
    {
        public override string ToString() => Label;
    }
}
