using System.Net;

namespace CQEING.Gestor.Core.Tests.Apoyo;

/// <summary>Cuerpos de respuesta HTTP que fallan a mitad de camino, para probar descargas cortadas.</summary>
internal static class FlujosFalsos
{
    /// <summary>Entrega <paramref name="enviar"/> bytes y después falla como una conexión que se cae.</summary>
    public static HttpResponseMessage Cortada(byte[] datos, int enviar) =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new FlujoQueFalla(datos, enviar, colgarse: false)) };

    /// <summary>Entrega <paramref name="enviar"/> bytes y después no manda nada más (hasta que se cancele).</summary>
    public static HttpResponseMessage Colgada(byte[] datos, int enviar) =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new FlujoQueFalla(datos, enviar, colgarse: true)) };

    private sealed class FlujoQueFalla(byte[] datos, int enviar, bool colgarse) : Stream
    {
        private int _posicion;

        public override async ValueTask<int> ReadAsync(Memory<byte> destino, CancellationToken ct = default)
        {
            if (_posicion >= enviar)
            {
                if (colgarse) await Task.Delay(Timeout.Infinite, ct);
                throw new IOException("Se cortó la conexión.");
            }
            int n = Math.Min(Math.Min(destino.Length, enviar - _posicion), 16);
            datos.AsMemory(_posicion, n).CopyTo(destino);
            _posicion += n;
            return n;
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
