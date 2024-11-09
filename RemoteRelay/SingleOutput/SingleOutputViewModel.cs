using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using RemoteRelay.Controls;

namespace RemoteRelay.SingleOutput;

public class SingleOutputViewModel : ViewModelBase
{
    private readonly AppSettings _settings;

    public SingleOutputViewModel(AppSettings settings)
    {
        _settings = settings;
        Sources = _settings.Sources.Select(x => new SourceButtonViewModel(x));
        //var bar = new Subject<SourceButtonViewModel>();

        var foo = Sources.Select(x => x.IsSelected.Select(_ => x)).Merge();
        var timeout = foo.TakeLast(1).Delay(TimeSpan.FromSeconds(10)).Select(_ => (SourceButtonViewModel)null!);
        var selected = foo.Merge(timeout);
        selected.Subscribe(x => x.SetSelectedColour());
        selected.SkipLast(1).Subscribe(x => x.SetInactiveColour());
    }

    public IEnumerable<SourceButtonViewModel> Sources { get; }
}

// a  b      a     c       a
//   .  .      .     .       .

// a b c
//    . . .
// a b.c. .
// a b c  .