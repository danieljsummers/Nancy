namespace Nancy.Session.RethinkDb
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    using Nancy.Cryptography;
    using global::RethinkDb.Driver.Net;

    /// <summary>
    /// Configuration for RethinkDB session storage
    /// </summary>
    public class RethinkDbSessionConfiguration : CookieBasedSessionsConfiguration
    {
        /// <summary>
        /// Default RethinkDB database name for session storage
        /// </summary>
        internal const string DefaultSessionDatabase = "NancySession";

        /// <summary>
        /// Default RethinkDB table name for session storage
        /// </summary>
        internal const string DefaultSessionTable = "Session";

        /// <summary>
        /// Use rolling sessions by default
        /// </summary>
        internal const bool DefaultRollingSessions = true;

        /// <summary>
        /// Default session duration (2 hours)
        /// </summary>
        internal static TimeSpan DefaultExpiry = new TimeSpan(2, 0, 0);

        /// <summary>
        /// Default frequency of checking for expired sessions (1 minute)
        /// </summary>
        internal static TimeSpan DefaultExpiryCheckFrequency = new TimeSpan(0, 1, 0);

        /// <summary>
        /// Initializes a new instance of the <see cref="RethinkDbSessionConfiguration"/> class.
        /// </summary>
        public RethinkDbSessionConfiguration() : this(null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RethinkDbSessionConfiguration"/> class.
        /// </summary>
        /// <param name="connection">The RethinkDB connection to use for session storage</param>
        public RethinkDbSessionConfiguration(IConnection connection) : this(connection, CryptographyConfiguration.Default)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RethinkDbSessionConfiguration"/> class.
        /// </summary>
        /// <param name="connection">The RethinkDB connection to use for session storage</param>
        /// <param name="cryptographyConfiguration">Security configuration for the session cookie</param>
        public RethinkDbSessionConfiguration(IConnection connection, CryptographyConfiguration cryptographyConfiguration)
            : base(cryptographyConfiguration)
        {
            this.Connection = connection;
            this.Database = DefaultSessionDatabase;
            this.Table = DefaultSessionTable;
            this.Expiry = DefaultExpiry;
            this.ExpiryCheckFrequency = DefaultExpiryCheckFrequency;
            this.UseRollingSessions = DefaultRollingSessions;
        }

        /// <summary>
        /// Gets or sets the name of the RethinkDB database to use for session storage
        /// </summary>
        public string Database { get; set; }

        /// <summary>
        /// Gets or sets the name of the RethinkDB table to use for session storage
        /// </summary>
        public string Table { get; set; }

        /// <summary>
        /// Gets or sets the RethinkDB connection to use for session storage
        /// </summary>
        public IConnection Connection { get; set; }

        /// <summary>
        /// Gets or sets the session expiry period
        /// </summary>
        public TimeSpan Expiry { get; set; }

        /// <summary>
        /// Gets or sets the frequency with which expired sessions are removed from storage
        /// </summary>
        public TimeSpan ExpiryCheckFrequency { get; set; }

        /// <summary>
        /// Gets or sets whether to use rolling sessions (expiry based on inactivity) or not (expiry based on creation)
        /// </summary>
        public bool UseRollingSessions { get; set; }

        /// <summary>
        /// Returns a value indicating whether the configuration is valid
        /// </summary>
        public override bool IsValid
        {
            get
            {
                if (!base.IsValid)
                {
                    return false;
                }

                if (String.IsNullOrEmpty(this.Database))
                {
                    return false;
                }

                if (String.IsNullOrEmpty(this.Table))
                {
                    return false;
                }

                if (null == this.Connection)
                {
                    return false;
                }

                return true;
            }
        }
    }
}