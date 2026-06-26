using SistemaDeStockV3.Models;

namespace SistemaDeStockV3.Tests;

public class DataService_ClientesTests
{
    [Fact]
    public async Task SaveClienteAsync_Crea_NuevoCliente_Y_CuentaCorriente()
    {
        // Regla de negocio: al crear un cliente, se crea automáticamente una CuentaCorriente asociada
        var (svc, _) = TestDbHelper.Create(nameof(SaveClienteAsync_Crea_NuevoCliente_Y_CuentaCorriente));
        var cliente = new Cliente { Name = "Juan Pérez", Phone = "11-1234-5678" };

        await svc.SaveClienteAsync(cliente);

        var clientes = await svc.GetClientesAsync();
        var cc = await svc.GetCuentaCorrienteAsync(cliente.Id);

        Assert.Single(clientes);
        Assert.NotNull(cc);
        Assert.Equal(0, cc.Balance); // La CC arranca en cero
    }

    [Fact]
    public async Task SaveClienteAsync_Actualiza_ClienteExistente()
    {
        var (svc, _) = TestDbHelper.Create(nameof(SaveClienteAsync_Actualiza_ClienteExistente));
        var cliente = new Cliente { Name = "Original", Phone = "000" };
        await svc.SaveClienteAsync(cliente);

        cliente.Name = "Actualizado";
        cliente.Phone = "999";
        await svc.SaveClienteAsync(cliente);

        var result = await svc.GetClientesAsync();
        Assert.Single(result);
        Assert.Equal("Actualizado", result[0].Name);
        Assert.Equal("999", result[0].Phone);
    }

    [Fact]
    public async Task DeleteClienteAsync_Elimina_Cliente_Y_SuCuentaCorriente()
    {
        // Regla de negocio: eliminar un cliente también elimina su CuentaCorriente
        var (svc, _) = TestDbHelper.Create(nameof(DeleteClienteAsync_Elimina_Cliente_Y_SuCuentaCorriente));
        var cliente = new Cliente { Name = "A Borrar" };
        await svc.SaveClienteAsync(cliente);

        await svc.DeleteClienteAsync(cliente.Id);

        var clientes = await svc.GetClientesAsync();
        var cc = await svc.GetCuentaCorrienteAsync(cliente.Id);

        Assert.Empty(clientes);
        Assert.Null(cc); // La CC también debería haberse eliminado
    }

    [Fact]
    public async Task SaveCuentaCorrienteAsync_ActualizaBalance_Correctamente()
    {
        var (svc, _) = TestDbHelper.Create(nameof(SaveCuentaCorrienteAsync_ActualizaBalance_Correctamente));
        var cliente = new Cliente { Name = "Test Cliente" };
        await svc.SaveClienteAsync(cliente);

        var cc = await svc.GetCuentaCorrienteAsync(cliente.Id);
        Assert.NotNull(cc);

        cc.Balance = 15000;
        await svc.SaveCuentaCorrienteAsync(cc);

        var ccActualizado = await svc.GetCuentaCorrienteAsync(cliente.Id);
        Assert.Equal(15000, ccActualizado!.Balance);
    }

    [Fact]
    public async Task GetClientesAsync_RetornaOrdenado_PorNombre()
    {
        var (svc, _) = TestDbHelper.Create(nameof(GetClientesAsync_RetornaOrdenado_PorNombre));
        await svc.SaveClienteAsync(new Cliente { Name = "Zoe Martínez" });
        await svc.SaveClienteAsync(new Cliente { Name = "Ana López" });

        var result = await svc.GetClientesAsync();

        Assert.Equal("Ana López", result[0].Name);
        Assert.Equal("Zoe Martínez", result[1].Name);
    }
}
