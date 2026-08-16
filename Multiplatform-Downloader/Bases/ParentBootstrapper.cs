using Autofac;
using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Multiplatform_Downloader.Bases;
/****************************************************************************
       Purpose      : This class is an abstract class that governs 
                      the process of registering and starting 
                      Caliburn.micro's instances.
       Created On   : 2026-07-30 오전 10:05:59
    ****************************************************************************/

public abstract class ParentBootstrapper<T> : BootstrapperBase where T : class
{

    #region - Ctors -
    public ParentBootstrapper()
    {
        CancellationTokenSourceHandler = new CancellationTokenSource();
    }
    #endregion
    #region - Implementation of Interface -
    #endregion
    #region - Overrides -
    protected abstract Task Start();
    protected abstract Task Stop();
    protected abstract void ConfigureContainer(ContainerBuilder builder);

    protected override async void OnStartup(object sender, StartupEventArgs e)
    {
        base.OnStartup(sender, e);
        await Start();
    }

    protected override async void OnExit(object sender, EventArgs e)
    {
        base.OnExit(sender, e);
        await Stop();
    }

    protected override void Configure()
    {
        ContainerBuilder? builder = new ContainerBuilder();

        RegisterBaseType(builder);
        builder.RegisterType<T>().SingleInstance();
        ConfigureContainer(builder);

        _container = builder?.Build();
    }


    private void RegisterBaseType(ContainerBuilder builder)
    {
        builder.RegisterType<WindowManager>().AsImplementedInterfaces().SingleInstance();
        builder.RegisterType<EventAggregator>().AsImplementedInterfaces().SingleInstance();
    }

    protected override object? GetInstance(Type service, string key)
    {
        var container = Container ?? throw new InvalidOperationException("Container has not been built yet.");

        if (string.IsNullOrWhiteSpace(key))
        {
            if (container.IsRegistered(service))
                return container.Resolve(service);
        }
        else
        {
            if (container.IsRegisteredWithKey(key, service))
                return container.ResolveKeyed(key, service);
        }
        throw new Exception(string.Format("Could not locate any instances of contract {0}.", key ?? service.Name));

    }

    protected override IEnumerable<object>? GetAllInstances(Type service)
    {
        return Container?.Resolve(typeof(IEnumerable<>).MakeGenericType(service)) as IEnumerable<object>;
    }

    protected override void BuildUp(object instance)
    {
        Container?.InjectProperties(instance);
    }


    #endregion
    #region - Binding Methods -
    #endregion
    #region - Processes -
    #endregion
    #region - IHanldes -
    #endregion
    #region - Properties -
    static public IContainer? Container => _container;
    protected CancellationTokenSource CancellationTokenSourceHandler { get; }
    #endregion
    #region - Attributes -
    static private IContainer? _container;
    #endregion
}
