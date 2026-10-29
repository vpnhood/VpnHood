using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Server.ProbeResistance;

// the server's connections that haven't proven a key: from their accept, through the TLS handshake and the web
// front's keep-alive, until a session takes them; port 80's never prove one. Above the cap, the address that holds
// the most loses its oldest, so a flood from one machine costs that machine first: closing the oldest overall would
// close every connection before its key arrived. An address is its DoS key, an IPv6 one by its /64
internal sealed class UnauthenticatedConnections
{
    private readonly Lock _lock = new();
    private readonly Dictionary<IPEndPointPair, Entry> _entries = [];
    private readonly Dictionary<string, LinkedList<Entry>> _byAddress = [];

    private int _maxCount = 1000;

    public int MaxCount {
        get {
            lock (_lock)
                return _maxCount;
        }
        set {
            var evicted = new List<Entry>();
            lock (_lock) {
                _maxCount = value;
                while (_entries.Count > Math.Max(_maxCount, 1))
                    evicted.Add(RemoveOldestOfBusiest());
            }
            foreach (var entry in evicted)
                entry.Connection.Dispose();
        }
    }

    public int Count {
        get {
            lock (_lock)
                return _entries.Count;
        }
    }

    // a new connection; the cap may then close one, this one included
    public void Add(IPEndPointPair endPoints, IDisposable connection)
    {
        Entry? evicted = null;
        Entry? replaced = null;
        lock (_lock) {
            if (_entries.TryGetValue(endPoints, out var existing)) {
                if (ReferenceEquals(existing.Connection, connection))
                    return;
                _entries.Remove(endPoints);
                RemoveFromAddress(existing);
                replaced = existing;
            }

            var addressKey = IPAddressUtil.GetDosKey(endPoints.RemoteEndPoint.Address);
            if (!_byAddress.TryGetValue(addressKey, out var addressEntries)) {
                addressEntries = new LinkedList<Entry>();
                _byAddress.Add(addressKey, addressEntries);
            }

            var entry = new Entry(endPoints, addressKey, connection);
            entry.Node = addressEntries.AddLast(entry);
            _entries.Add(endPoints, entry);
            if (_entries.Count > Math.Max(_maxCount, 1))
                evicted = RemoveOldestOfBusiest();
        }

        replaced?.Connection.Dispose();
        evicted?.Connection.Dispose();
    }

    // a connection that proved its key, or ended
    public void Remove(IPEndPointPair endPoints, IDisposable connection)
    {
        lock (_lock) {
            if (!_entries.TryGetValue(endPoints, out var entry) || !ReferenceEquals(entry.Connection, connection))
                return;
            _entries.Remove(endPoints);
            RemoveFromAddress(entry);
        }
    }

    private Entry RemoveOldestOfBusiest()
    {
        var busiest = _byAddress.Values.MaxBy(x => x.Count) ??
                      throw new InvalidOperationException("There is no connection to close.");
        var oldest = busiest.First?.Value ?? throw new InvalidOperationException("An address has no connection.");
        _entries.Remove(oldest.EndPoints);
        RemoveFromAddress(oldest);
        return oldest;
    }

    private void RemoveFromAddress(Entry entry)
    {
        if (entry.Node is not { List: { } addressEntries } node)
            return;

        addressEntries.Remove(node);
        if (addressEntries.Count == 0)
            _byAddress.Remove(entry.AddressKey);
    }

    private sealed class Entry(IPEndPointPair endPoints, string addressKey, IDisposable connection)
    {
        public IPEndPointPair EndPoints => endPoints;
        public string AddressKey => addressKey;
        public IDisposable Connection => connection;
        public LinkedListNode<Entry>? Node { get; set; }
    }
}
