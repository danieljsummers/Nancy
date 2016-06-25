namespace Nancy.Session.RethinkDb
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    using global::RethinkDb.Driver;
    using global::RethinkDb.Driver.Ast;

    /// <summary>
    /// Methods used for interacting with RethinkDB's session store
    /// </summary>
    public static class RethinkDbSessionStore
    {
        private static RethinkDB R = RethinkDB.R;

        /// <summary>
        /// Create a new session
        /// </summary>
        /// <param name="configuration">The session store configuration</param>
        /// <param name="id">The Id of the session to create</param>
        /// <exception cref="InvalidOperationException">If the session cannot be created</exception>
        public static async Task NewSession(RethinkDbSessionConfiguration configuration, string id)
        {
            var result = await Table(configuration).Insert(new RethinkDbSessionDocument
            {
                Id = id,
                LastAccessed = DateTime.Now,
                Data = new Dictionary<string, object>()
            }).RunResultAsync(configuration.Connection);

            if (0 != result.Errors)
            {
                throw new InvalidOperationException(String.Format("Could not create new session Id {0}: {1}", id,
                    result.FirstError));
            }
        }

        /// <summary>
        /// Retrieve a session from the store
        /// </summary>
        /// <param name="configuration">The session store configuration</param>
        /// <param name="id">The Id of the session to retrieve</param>
        /// <returns>The session (null if a session with the given Id is not found)</returns>
        public static async Task<ISession> RetrieveSession(RethinkDbSessionConfiguration configuration, string id)
        {
            var doc = await Table(configuration).Get(id)
                .RunAtomAsync<RethinkDbSessionDocument>(configuration.Connection);

            if (null == doc)
            {
                return null;
            }

            var session = new Session(doc.Data);
            session["_id"] = id;

            return session;
        }

        /// <summary>
        /// Update the last accessed date/time for a session
        /// </summary>
        /// <param name="configuration">The session store configuration</param>
        /// <param name="id">The Id of the session to be updated</param>
        /// <exception cref="InvalidOperationException">If there is an error updating the session</exception>
        public static async Task UpdateLastAccessed(RethinkDbSessionConfiguration configuration, string id)
        {
            var result = await Table(configuration).Get(id).Update(new { LastAccessed = DateTime.Now })
                .RunResultAsync(configuration.Connection);

            if (0 != result.Errors)
            {
                throw new InvalidOperationException(String.Format("Could not update last access for session Id {0}: {1}",
                    id, result.FirstError));
            }
        }

        /// <summary>
        /// Update the session
        /// </summary>
        /// <param name="configuration">The session store configuration</param>
        /// <param name="id">The Id of the session</param>
        /// <param name="session">The session to be persisted</param>
        /// <exception cref="InvalidOperationException">If the update errors</exception>
        public static async Task UpdateSession(RethinkDbSessionConfiguration configuration, string id, ISession session)
        {
            var sessionDocument = new RethinkDbSessionDocument
            {
                Id = id,
                LastAccessed = DateTime.Now,
                Data = session.ToDictionary(item => item.Key, item => item.Value)
            };

            Func<Get, Update> update = get => {
                if (configuration.UseRollingSessions)
                {
                    return get.Update(new
                    {
                        LastAccessed = sessionDocument.LastAccessed,
                        Data = sessionDocument.Data
                    });
                }
                else
                {
                    return get.Update(new { Data = sessionDocument.Data });
                }
            };

            var result = await update(Table(configuration).Get(id)).RunResultAsync(configuration.Connection);

            if (0 != result.Errors)
            {
                throw new InvalidOperationException(String.Format("Unable to save data for session Id {0}: {1}", id,
                    result.FirstError));
            }
        }

        /// <summary>
        /// Expire (delete) old sessions
        /// </summary>
        /// <param name="configuration">The session store configuration</param>
        /// <exception cref="InvalidOperationException">If there is an error deleting old sessions</exception>
        public static async Task ExpireSessions(RethinkDbSessionConfiguration configuration)
        {
            var result = await Table(configuration).Between(R.Minval(), DateTime.Now - configuration.Expiry)
                .OptArg("index", nameof(RethinkDbSessionDocument.LastAccessed))
                .Delete()
                .RunResultAsync(configuration.Connection);

            if (0 != result.Errors)
            {
                throw new InvalidOperationException(String.Concat("Error expiring sessions: ", result.FirstError));
            }
        }

        /// <summary>
        /// Set up the session store; ensures the database, table, and required indexes exist
        /// </summary>
        /// <param name="configuration">The session store configuration</param>
        public static async Task SetUp(RethinkDbSessionConfiguration configuration)
        {
            await DatabaseCheck(configuration);
            await TableCheck(configuration);
        }

        /// <summary>
        /// Create the database if it does not exist
        /// </summary>
        /// <param name="configuration">The session store configuration</param>
        /// <exception cref="InvalidOperationException">If there is a problem creating the database</exception>
        private static async Task DatabaseCheck(RethinkDbSessionConfiguration configuration)
        {
            var databases = await R.DbList().RunAtomAsync<List<string>>(configuration.Connection);

            if (!databases.Contains(configuration.Database))
            {
                var result = await R.DbCreate(configuration.Database).RunResultAsync(configuration.Connection);

                if (0 != result.Errors)
                {
                    throw new InvalidOperationException(String.Format(
                        "Could not create RethinkDB session store database {0}: {1}", configuration.Database,
                        result.FirstError));
                }
            }
        }

        /// <summary>
        /// Create the table if it does not exist
        /// </summary>
        /// <param name="configuration">The session store configuration</param>
        /// <exception cref="InvalidOperationException">If there is a problem creating the table</exception>
        private static async Task TableCheck(RethinkDbSessionConfiguration configuration)
        {
            var tables = await R.Db(configuration.Database).TableList().RunAtomAsync<List<string>>(configuration.Connection);

            if (!tables.Contains(configuration.Table))
            {
                var result = await R.Db(configuration.Database).TableCreate(configuration.Table)
                    .RunResultAsync(configuration.Connection);

                if (0 != result.Errors)
                {
                    throw new InvalidOperationException(String.Format(
                        "Could not create RethinkDB session store table {0}.{1}: {2}", configuration.Database,
                        configuration.Table, result.FirstError));
                }
            }
        }

        /// <summary>
        /// Create the index on the last accessed date/time if it does not exist
        /// </summary>
        /// <param name="configuration">The session store configuration</param>
        /// <exception cref="InvalidOperationException">If there is a problem creating the index</exception>
        private static async Task IndexCheck(RethinkDbSessionConfiguration configuration)
        {
            var indexName = nameof(RethinkDbSessionDocument.LastAccessed);
            var indexes = await Table(configuration).IndexList()
                .RunAtomAsync<List<string>>(configuration.Connection);

            if (!indexes.Contains(indexName))
            {
                var result = await R.Db(configuration.Database).Table(configuration.Table).IndexCreate(indexName)
                    .RunResultAsync(configuration.Connection);

                if (0 != result.Errors)
                {
                    throw new InvalidOperationException(String.Format(
                        "Could not create last accessed index on RethinkDB session store table {0}.{1}: {2}",
                        configuration.Database, configuration.Table, result.FirstError));
                }
            }
        }

        /// <summary>
        /// Shorthand to get the session table
        /// </summary>
        /// <param name="configuration">The session store configuration</param>
        /// <returns>The table reference for further manipulation</returns>
        private static Table Table(RethinkDbSessionConfiguration configuration) =>
            R.Db(configuration.Database).Table(configuration.Table);
    }
}