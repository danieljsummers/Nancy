namespace Nancy.Session.RethinkDb
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// POCO used to persist sessions in the RethinkDB session store
    /// </summary>
    public class RethinkDbSessionDocument
    {
        /// <summary>
        /// The Id of the session
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The date/time this session was last accessed
        /// </summary>
        public DateTime LastAccessed { get; set; }

        /// <summary>
        /// The data for the session
        /// </summary>
        public IDictionary<string, object> Data { get; set; }
    }
}