﻿using System;
using System.Collections.Generic;

#nullable disable

namespace VpnHood.AccessServer.Models
{
    public partial class Server
    {
        public Server()
        {
            ServerEndPoints = new HashSet<ServerEndPoint>();
        }

        public Guid ServerId { get; set; }
        public string ServerName { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime LastStatusTime { get; set; }
        public int LastSessionCount { get; set; }
        public string Description { get; set; }

        public virtual ICollection<ServerEndPoint> ServerEndPoints { get; set; }
    }
}
