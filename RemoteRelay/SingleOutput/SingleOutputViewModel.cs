using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using RemoteRelay.Controls;

namespace RemoteRelay.SingleOutput;

public class SingleOutputViewModel : ViewModelBase
{
   private readonly AppSettings _settings;
   private readonly TimeSpan _timeout = TimeSpan.FromSeconds(3);
   private readonly IObservable<SourceButtonViewModel?> _selected;


   public SingleOutputViewModel(AppSettings settings)
   {
      _settings = settings;
      Inputs = _settings.Sources.Select(x => new SourceButtonViewModel(x)).ToArray();


      //Debug.WriteLine("Lets go for observables");

      //var a = Inputs.Select(x => x.IsSelected.Subscribe(_ => Debug.WriteLine("Item Clicked"))).ToArray();

      _selected =
            Inputs
                .Select(
                    x => x.IsSelected.Select(_ => x))
                .Merge()
                .Select(x =>
                    Observable
                        .Return(x)
                        .Merge(
                            Observable
                                .Return((SourceButtonViewModel)null)
                                .Delay(_timeout)))
                .Merge(Cancel.IsSelected.Select(_ => Observable.Return((SourceButtonViewModel)null)))
                .Switch()
                .Scan((a, b) => a != b ? b : null);

      var a = Output.IsSelected
          .WithLatestFrom(
         _selected,
         (a, b) => (a, b))
          .Where(x => x.b != null);


    //  Inputs
    //.Select(
    //    x => x.IsSelected.Select(_ => x))
    //.Merge().Subscribe(x => Debug.WriteLine(x.SourceName));

      _ = _selected.Subscribe(x =>
      {
         x?.SetSelectedColour();
         Debug.WriteLine(x?.SourceName);
      });
      _ = _selected.SkipLast(1).Subscribe(x =>
      {
         if(x is not null)
            x.SetInactiveColour();
      });
   }

   public IEnumerable<SourceButtonViewModel> Inputs { get; }

   public SourceButtonViewModel Output { get; } = new("Confirm");

   public SourceButtonViewModel Cancel { get; } = new("Cancel");

}
