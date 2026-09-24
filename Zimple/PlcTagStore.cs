using libplctag;
using libplctag.DataTypes;
using libplctag.DataTypes.Simple;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Zimple
{

    public sealed class PlcTagStore : IDisposable
    {
        private readonly string _ip;
        private readonly ConcurrentDictionary<string, ITag> _tags;
        private readonly object _syncRoot = new object();
        private bool _disposed;
        private long _readOperations;
        private long _writeOperations;

        public PlcTagStore(string ip)
        {
            _ip = ip;
            _tags = new ConcurrentDictionary<string, ITag>();
        }

        // -------------------------
        // TAG ACCESS (FACTORY + CACHE)
        // -------------------------

        public TagDint GetDint(string name)
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                return (TagDint)_tags.GetOrAdd(name, key => CreateDint(key));
            }
        }

        public TagDint GetHeartbeat(string name)
        {
            // semantic alias, no special behavior
            return GetDint(name);
        }

        public Tag<StringPlcMapper, string> GetString(string name)
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                return (Tag<StringPlcMapper, string>)_tags.GetOrAdd(
                    name, key => CreateString(key));
            }
        }

        public Tag<StringPlcMapper, string[]> GetStringArray(string name, int length)
        {
            string key = name + "[" + length + "]";
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                return (Tag<StringPlcMapper, string[]>)_tags.GetOrAdd(
                    key, k => CreateStringArray(name, length));
            }
        }

        public Tag<BoolPlcMapper, bool> GetBool(string name)
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                return (Tag<BoolPlcMapper, bool>)_tags.GetOrAdd(
                    name, key => CreateBool(key));
            }
        }

        // -------------------------
        // READ / WRITE HELPERS
        // -------------------------

        public int ReadDint(TagDint tag)
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                Interlocked.Increment(ref _readOperations);
                return ExecutePlcAccess("read", tag, () =>
                {
                    tag.Read();
                    return tag.Value;
                });
            }
        }

        public void WriteDint(TagDint tag, int value)
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                Interlocked.Increment(ref _writeOperations);
                ExecutePlcAccess("write", tag, () =>
                {
                    tag.Value = value;
                    tag.Write();
                });
            }
        }

        public string ReadString(Tag<StringPlcMapper, string> tag)
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                Interlocked.Increment(ref _readOperations);
                return ExecutePlcAccess("read", tag, () =>
                {
                    tag.Read();
                    return tag.Value ?? string.Empty;
                });
            }
        }

        public void WriteString(Tag<StringPlcMapper, string> tag, string value)
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                Interlocked.Increment(ref _writeOperations);
                ExecutePlcAccess("write", tag, () =>
                {
                    tag.Value = value ?? string.Empty;
                    tag.Write();
                });
            }
        }

        public bool ReadBool(Tag<BoolPlcMapper, bool> tag)
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                Interlocked.Increment(ref _readOperations);
                return ExecutePlcAccess("read", tag, () =>
                {
                    tag.Read();
                    return tag.Value;
                });
            }
        }

        public string[] ReadStringArray(Tag<StringPlcMapper, string[]> tag)
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                Interlocked.Increment(ref _readOperations);
                return ExecutePlcAccess("read", tag, () =>
                {
                    tag.Read();
                    return tag.Value ?? new string[0];
                });
            }
        }

        public void WriteStringArray(Tag<StringPlcMapper, string[]> tag, string[] value)
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                Interlocked.Increment(ref _writeOperations);
                ExecutePlcAccess("write", tag, () =>
                {
                    tag.Value = value ?? new string[0];
                    tag.Write();
                });
            }
        }

        public PlcOperationSnapshot SnapshotOperations()
        {
            return new PlcOperationSnapshot(
                Interlocked.Read(ref _readOperations),
                Interlocked.Read(ref _writeOperations));
        }

        // -------------------------
        // TAG FACTORIES
        // -------------------------

        private TagDint CreateDint(string name)
        {
            TagDint tag = new TagDint
            {
                Name = name,
                Gateway = _ip,
                Path = "1,0",
                PlcType = PlcType.ControlLogix,
                Protocol = Protocol.ab_eip,
                Timeout = TimeSpan.FromSeconds(3)
            };

            ExecutePlcAccess("initialize", tag, () => tag.Initialize());
            return tag;
        }

        private Tag<StringPlcMapper, string> CreateString(string name)
        {
            Tag<StringPlcMapper, string> tag =
                new Tag<StringPlcMapper, string>
                {
                    Name = name,
                    Gateway = _ip,
                    Path = "1,0",
                    PlcType = PlcType.ControlLogix,
                    Protocol = Protocol.ab_eip,
                    Timeout = TimeSpan.FromSeconds(3)
                };

            ExecutePlcAccess("initialize", tag, () => tag.Initialize());
            return tag;
        }

        private Tag<StringPlcMapper, string[]> CreateStringArray(string name, int length)
        {
            Tag<StringPlcMapper, string[]> tag =
                new Tag<StringPlcMapper, string[]>
                {
                    Name = name,
                    Gateway = _ip,
                    Path = "1,0",
                    PlcType = PlcType.ControlLogix,
                    Protocol = Protocol.ab_eip,
                    Timeout = TimeSpan.FromSeconds(3),
                    ArrayDimensions = new[] { length }
                };

            ExecutePlcAccess("initialize", tag, () => tag.Initialize());
            return tag;
        }

        private Tag<BoolPlcMapper, bool> CreateBool(string name)
        {
            Tag<BoolPlcMapper, bool> tag =
                new Tag<BoolPlcMapper, bool>
                {
                    Name = name,
                    Gateway = _ip,
                    Path = "1,0",
                    PlcType = PlcType.ControlLogix,
                    Protocol = Protocol.ab_eip,
                    Timeout = TimeSpan.FromSeconds(3)
                };

            ExecutePlcAccess("initialize", tag, () => tag.Initialize());
            return tag;
        }

        // -------------------------
        // LIFECYCLE
        // -------------------------

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                    return;

                _disposed = true;

                foreach (ITag tag in _tags.Values)
                {
                    try
                    {
                        tag.Dispose();
                    }
                    catch
                    {
                        // ignore shutdown errors
                    }
                }

                _tags.Clear();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PlcTagStore));
        }

        private void ExecutePlcAccess(string operation, ITag tag, Action action)
        {
            ExecutePlcAccess<object>(operation, tag, () =>
            {
                action();
                return null;
            });
        }

        private T ExecutePlcAccess<T>(string operation, ITag tag, Func<T> action)
        {
            try
            {
                return action();
            }
            catch (LibPlcTagException ex)
            {
                throw new PlcCommunicationException(_ip, GetTagName(tag), operation, ex);
            }
        }

        private static string GetTagName(ITag tag)
        {
            if (tag == null)
                return "<null>";

            try
            {
                object value = tag.GetType().GetProperty("Name")?.GetValue(tag, null);
                return value as string ?? tag.GetType().Name;
            }
            catch
            {
                return tag.GetType().Name;
            }
        }
    }

    public sealed class PlcCommunicationException : Exception
    {
        public PlcCommunicationException(string ip, string tagName, string operation, Exception innerException)
            : base($"PLC communication failed during {operation} on {ip}, tag '{tagName}': {innerException.Message}", innerException)
        {
            Ip = ip;
            TagName = tagName;
            Operation = operation;
        }

        public string Ip { get; }
        public string TagName { get; }
        public string Operation { get; }
    }

    public sealed class PlcOperationSnapshot
    {
        public PlcOperationSnapshot(long reads, long writes)
        {
            Reads = reads;
            Writes = writes;
        }

        public long Reads { get; }
        public long Writes { get; }
    }
}
