namespace Nancy.Session.RethinkDb
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using Nancy;
    using Nancy.Bootstrapper;
    using Nancy.Cookies;
    using Nancy.Cryptography;
    using Nancy.Helpers;

    using global::RethinkDb.Driver.Net;

    /// <summary>
    /// Sessions backed by RethinkDB
    /// </summary>
    public class RethinkDbSessions
    {
        /// <summary>
        /// Current configuration for session storage
        /// </summary>
        private readonly RethinkDbSessionConfiguration currentConfiguration;

        /// <summary>
        /// The date/time we last cleaned out expired sessions
        /// </summary>
        private DateTime LastExpiryCheck { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="RethinkDbSessions"/> class.
        /// </summary>
        /// <param name="connection">The RethinkDb connection to use for session storage</param>
        /// <param name="encryptionProvider">The encryption provider.</param>
        /// <param name="hmacProvider">The hmac provider</param>
        public RethinkDbSessions(IConnection connection, IEncryptionProvider encryptionProvider, IHmacProvider hmacProvider)
            : this(new RethinkDbSessionConfiguration
                {
                    Connection = connection,
                    Serializer = new DefaultObjectSerializer(),
                    CryptographyConfiguration = new CryptographyConfiguration(encryptionProvider, hmacProvider)
                })
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RethinkDbSessions"/> class.
        /// </summary>
        /// <param name="configuration">Cookie based sessions configuration.</param>
        public RethinkDbSessions(RethinkDbSessionConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException("configuration");
            }

            if (!configuration.IsValid)
            {
                throw new ArgumentException("Configuration is invalid", "configuration");
            }
            this.currentConfiguration = configuration;

            this.Await(RethinkDbSessionStore.SetUp(this.currentConfiguration));
        }

        /// <summary>
        /// Initialise and add RethinkDB session storage hooks to the application pipeline
        /// </summary>
        /// <param name="pipelines">Application pipelines</param>
        /// <param name="configuration">Cookie based sessions configuration.</param>
        public static void Enable(IPipelines pipelines, RethinkDbSessionConfiguration configuration)
        {
            if (pipelines == null)
            {
                throw new ArgumentNullException("pipelines");
            }

            var sessionStore = new RethinkDbSessions(configuration);

            pipelines.BeforeRequest.AddItemToStartOfPipeline(ctx => LoadSession(ctx, sessionStore));
            pipelines.AfterRequest.AddItemToEndOfPipeline(ctx => SaveSession(ctx, sessionStore));
        }

        /// <summary>
        /// Initialise and add RethinkDB session storage hooks to the application pipeline
        /// </summary>
        /// <param name="pipelines">Application pipelines</param>
        /// <param name="connection">The RethinkDB connection to use for session storage</param>
        /// <param name="cryptographyConfiguration">Cryptography configuration</param>
        public static void Enable(IPipelines pipelines, IConnection connection,
            CryptographyConfiguration cryptographyConfiguration)
        {
            var rethinkDbSessionConfiguration = new RethinkDbSessionConfiguration(connection, cryptographyConfiguration)
            {
                Serializer = new DefaultObjectSerializer()
            };
            Enable(pipelines, rethinkDbSessionConfiguration);
        }

        /// <summary>
        /// Initialise and add RethinkDB session storage hooks to the application pipeline with the default encryption provider.
        /// </summary>
        /// <param name="pipelines">Application pipelines</param>
        /// <param name="connection">The RethinkDB connection to use for session storage</param>
        /// <returns>Formatter selector for choosing a non-default serializer</returns>
        public static void Enable(IPipelines pipelines, IConnection connection)
        {
            Enable(pipelines, new RethinkDbSessionConfiguration
            {
                Connection = connection,
                Serializer = new DefaultObjectSerializer()
            });
        }

        /// <summary>
        /// Save the session into the response
        /// </summary>
        /// <param name="session">Session to save</param>
        /// <param name="response">Response to save into</param>
        public void Save(ISession session, Nancy.Response response)
        {
            this.ExpireOldSessions();

            if (session == null || !session.HasChanged)
            {
                return;
            }

            var id = session["_id"] as string;

            if (null == id)
            {
                // TODO: warn
                return;
            }

            // Persist the session
            session.Delete("_id");
            Await(RethinkDbSessionStore.UpdateSession(currentConfiguration, id, session));

            // Encrypt the session Id in the cookie
            var cryptographyConfiguration = this.currentConfiguration.CryptographyConfiguration;
            var encryptedData = cryptographyConfiguration.EncryptionProvider.Encrypt(id);
            var hmacBytes = cryptographyConfiguration.HmacProvider.GenerateHmac(encryptedData);
            var cookieData = HttpUtility.UrlEncode(String.Format("{0}{1}", Convert.ToBase64String(hmacBytes), encryptedData));

            var cookie = new NancyCookie(this.currentConfiguration.CookieName, cookieData, true)
            {
                Domain = this.currentConfiguration.Domain,
                Path = this.currentConfiguration.Path
            };
            response.WithCookie(cookie);
        }

        /// <summary>
        /// Loads the session from the request
        /// </summary>
        /// <param name="request">Request to load from</param>
        /// <returns>ISession containing the load session values</returns>
        public ISession Load(Request request)
        {
            this.ExpireOldSessions();

            var dictionary = new Dictionary<string, object>();

            // Get the session Id from the encrypted cookie
            var cookieName = this.currentConfiguration.CookieName;
            var hmacProvider = this.currentConfiguration.CryptographyConfiguration.HmacProvider;
            var encryptionProvider = this.currentConfiguration.CryptographyConfiguration.EncryptionProvider;

            if (!request.Cookies.ContainsKey(cookieName))
            {
                return CreateNewSession(dictionary);
            }

            var cookieData = HttpUtility.UrlDecode(request.Cookies[cookieName]);
            var hmacLength = Base64Helpers.GetBase64Length(hmacProvider.HmacLength);
            if (cookieData.Length < hmacLength)
            {
                return CreateNewSession(dictionary);
            }

            var hmacString = cookieData.Substring(0, hmacLength);
            var encryptedCookie = cookieData.Substring(hmacLength);

            var hmacBytes = Convert.FromBase64String(hmacString);
            var newHmac = hmacProvider.GenerateHmac(encryptedCookie);
            var hmacValid = HmacComparer.Compare(newHmac, hmacBytes, hmacProvider.HmacLength);

            if (!hmacValid)
            {
                return CreateNewSession(dictionary);
            }

            // Get the session itself from the database
            var id = encryptionProvider.Decrypt(encryptedCookie);
            var session = Await(RethinkDbSessionStore.RetrieveSession(currentConfiguration, id));

            if (null == session)
            {
                return CreateNewSession(dictionary);
            }

            if (currentConfiguration.UseRollingSessions)
            {
                Await(RethinkDbSessionStore.UpdateLastAccessed(currentConfiguration, id));
            }

            return session;
        }

        /// <summary>
        /// Create a new session
        /// </summary>
        /// <param name="dictionary">The dictionary to use for the session</param>
        /// <returns>The session object</returns>
        private ISession CreateNewSession(IDictionary<string, object> dictionary)
        {
            var id = Guid.NewGuid().ToString();
            Await(RethinkDbSessionStore.NewSession(currentConfiguration, id));

            var session = new Session(dictionary);
            session["_id"] = id;

            return session;
        }

        /// <summary>
        /// Expire old sessions
        /// </summary>
        public void ExpireOldSessions()
        {
            if (currentConfiguration.ExpiryCheckFrequency >= DateTime.Now - this.LastExpiryCheck)
            {
                return;
            }

            Await(RethinkDbSessionStore.ExpireSessions(currentConfiguration));

            this.LastExpiryCheck = DateTime.Now;
        }

        /// <summary>
        /// Saves the request session into the response
        /// </summary>
        /// <param name="context">Nancy context</param>
        /// <param name="sessionStore">Session store</param>
        private static void SaveSession(NancyContext context, RethinkDbSessions sessionStore)
        {
            sessionStore.Save(context.Request.Session, context.Response);
        }

        /// <summary>
        /// Loads the request session
        /// </summary>
        /// <param name="context">Nancy context</param>
        /// <param name="sessionStore">Session store</param>
        /// <returns>Always returns null</returns>
        private static Nancy.Response LoadSession(NancyContext context, RethinkDbSessions sessionStore)
        {
            if (context.Request == null)
            {
                return null;
            }

            context.Request.Session = sessionStore.Load(context.Request);

            return null;
        }

        /// <summary>
        /// Await the result of a non-generic task
        /// </summary>
        /// <param name="task">The task to await</param>
        private void Await(Task task) => task.GetAwaiter().GetResult();

        /// <summary>
        /// Await the result of a task
        /// </summary>
        /// <typeparam name="T">The type of object expected as a result</typeparam>
        /// <param name="task">The task to await</param>
        /// <returns>The result of the task</returns>
        private T Await<T>(Task<T> task) => task.GetAwaiter().GetResult();
    }
}