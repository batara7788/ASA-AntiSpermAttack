using System.Net;
using System.Runtime.InteropServices;

namespace Asa.App.Services;

public sealed record TcpEntry(int ProcessId, IPAddress LocalAddress, int LocalPort, IPAddress RemoteAddress, int RemotePort, bool Listening);

public static class NetworkTables
{
    private const int AddressFamilyIpv4 = 2;
    private const int AddressFamilyIpv6 = 23;
    private const int TableOwnerPidAll = 5;
    private const int StateListen = 2;
    private const int Ipv4RowSize = 24;
    private const int Ipv6RowSize = 56;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int addressFamily, int tableClass, uint reserved);

    public static List<TcpEntry> ReadTcpEntries()
    {
        var entries = new List<TcpEntry>();
        try
        {
            ReadTable(AddressFamilyIpv4, entries);
            ReadTable(AddressFamilyIpv6, entries);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        return entries;
    }

    private static void ReadTable(int addressFamily, List<TcpEntry> entries)
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, addressFamily, TableOwnerPidAll, 0);
        if (size <= 0)
        {
            return;
        }

        IntPtr buffer = Marshal.AllocHGlobal(size + 4096);
        try
        {
            size += 4096;
            if (GetExtendedTcpTable(buffer, ref size, false, addressFamily, TableOwnerPidAll, 0) != 0)
            {
                return;
            }

            int count = Marshal.ReadInt32(buffer);
            IntPtr row = buffer + 4;
            for (int index = 0; index < count; index++)
            {
                entries.Add(addressFamily == AddressFamilyIpv4 ? ReadIpv4Row(row) : ReadIpv6Row(row));
                row += addressFamily == AddressFamilyIpv4 ? Ipv4RowSize : Ipv6RowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static TcpEntry ReadIpv4Row(IntPtr row)
    {
        int state = Marshal.ReadInt32(row, 0);
        byte[] local = new byte[4];
        byte[] remote = new byte[4];
        Marshal.Copy(row + 4, local, 0, 4);
        Marshal.Copy(row + 12, remote, 0, 4);
        int localPort = ReadPort(row + 8);
        int remotePort = ReadPort(row + 16);
        int processId = Marshal.ReadInt32(row, 20);
        return new TcpEntry(processId, new IPAddress(local), localPort, new IPAddress(remote), remotePort, state == StateListen);
    }

    private static TcpEntry ReadIpv6Row(IntPtr row)
    {
        byte[] local = new byte[16];
        byte[] remote = new byte[16];
        Marshal.Copy(row, local, 0, 16);
        int localPort = ReadPort(row + 20);
        Marshal.Copy(row + 24, remote, 0, 16);
        int remotePort = ReadPort(row + 44);
        int state = Marshal.ReadInt32(row, 48);
        int processId = Marshal.ReadInt32(row, 52);
        return new TcpEntry(processId, new IPAddress(local), localPort, new IPAddress(remote), remotePort, state == StateListen);
    }

    private static int ReadPort(IntPtr pointer)
    {
        return (Marshal.ReadByte(pointer) << 8) | Marshal.ReadByte(pointer, 1);
    }
}
