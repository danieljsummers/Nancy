# Nancy.Session.RethinkDb

A RethinkDB-backed session store for use with Nancy, utilizing [RethinkDb.Driver](https://github.com/bchavez/RethinkDb.Driver).

## Use

The RethinkDB connection object is designed to have a multi-request lifetime. The example below registers a singleton
instance of a RethinkDB connection, then utilizes it to register the RethinkDB session store.

```csharp
public class ApplicationBootstrapper : DefaultNancyBootstrapper
{
	using Nancy.Bootstrapper;
    using Nancy.Session.RethinkDb;
	using Nancy.TinyIoC;
    using RethinkDb.Driver;
	using RethinkDb.Driver.Net;

	private static RethinkDB R = RethinkDB.R;

    public static IConnection Connection = R.Connection().[options].Connect();

    public override ApplicationStartup(TinyIoCContainer container, IPipelines pipelines)
	{
		// Register the connection
		container.Register<IConnection>(Connection);
		
		// Register the session store
		RethinkDbSessions.Enable(pipelines, container.Resolve<IConnection>());
	}
}
```

## Options

The example above uses all the defaults (a la SDHP); however, there is a complete configuration object you can pass to
control the behavior of the session store.  These options are in RethinkDbSessionConfiguration.

**Database** (default: "NancySession")

This is the RethinkDB database that will be used for persistence.

**Table** (default: "Session")

This is the table within the RethinkDB database that will be used for persistence.

**UseRollingSessions** (bool - default: true)

A rolling session is one where the expiration is reset every time the session is accessed, making the expiration into a
rolling window. A non-rolling session is only good from the time of its creation, and goes away at the end of that
period, no matter how active the session is.

**Expiry** (default: 2 hours)

This is how long (based on either creation or access time) the session will last.

**ExpiryCheckFrequency** (default: 1 minute)

This is how frequently expired sessions are deleted from the table.  Each attempt to load or save a session can trigger
this, but this throttle keeps the driver from doing excessive database I/O.

## About

This should be considered pre-alpha; use at your own risk. It is a notional design that is subject to revision at any
time.