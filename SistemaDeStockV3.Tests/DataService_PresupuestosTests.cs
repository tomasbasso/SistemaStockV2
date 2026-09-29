using Microsoft.EntityFrameworkCore;
using SistemaDeStockV3.Models;

namespace SistemaDeStockV3.Tests;

public class DataService_PresupuestosTests
{
    [Fact]
    public async Task SavePresupuestoAsync_CreaPresupuesto_ConDetalles_Y_LosPersiste()
    {
        // Regla de negocio: la cabecera y sus líneas se guardan juntas,
        // y el servicio asigna PresupuestoId a cada detalle.
        var (svc, _) = TestDbHelper.Create(nameof(SavePresupuestoAsync_CreaPresupuesto_ConDetalles_Y_LosPersiste));
        var presupuesto = new Presupuesto { Notas = "Presupuesto de prueba" };
        var detalles = new List<PresupuestoDetalle>
        {
            new() { ProductoId = Guid.NewGuid(), Quantity = 2, UnitPrice = 100.50m },
            new() { ProductoId = Guid.NewGuid(), Quantity = 1, UnitPrice = 300m },
        };

        await svc.SavePresupuestoAsync(presupuesto, detalles);

        var presupuestos = await svc.GetPresupuestosAsync();
        var detallesGuardados = await svc.GetPresupuestoDetallesAsync(presupuesto.Id);

        Assert.Single(presupuestos);
        Assert.Equal("Presupuesto de prueba", presupuestos[0].Notas);
        Assert.Equal(2, detallesGuardados.Count);
        Assert.All(detallesGuardados, d => Assert.Equal(presupuesto.Id, d.PresupuestoId));
    }

    [Fact]
    public async Task SavePresupuestoAsync_CalculaTotal_DesdeLosDetalles_IgnorandoElValorRecibido()
    {
        // El servicio recalcula Total = sum(UnitPrice * Quantity) en memoria (C#),
        // pisando cualquier valor que traiga la cabecera.
        var (svc, _) = TestDbHelper.Create(nameof(SavePresupuestoAsync_CalculaTotal_DesdeLosDetalles_IgnorandoElValorRecibido));
        var presupuesto = new Presupuesto { Total = 999999m }; // valor "mentiroso"
        var detalles = new List<PresupuestoDetalle>
        {
            new() { ProductoId = Guid.NewGuid(), Quantity = 3, UnitPrice = 10.25m }, // 30.75
            new() { ProductoId = Guid.NewGuid(), Quantity = 2, UnitPrice = 5.50m },  // 11.00
        };

        var guardado = await svc.SavePresupuestoAsync(presupuesto, detalles);

        Assert.Equal(41.75m, guardado.Total);

        // Verificamos también lo persistido, materializando primero (decimal es TEXT en SQLite:
        // no agregamos ni comparamos montos del lado del servidor).
        var persistido = (await svc.GetPresupuestosAsync()).Single();
        Assert.Equal(41.75m, persistido.Total);
    }

    [Fact]
    public async Task SavePresupuestoAsync_AsignaNumeroPresupuesto_Incremental()
    {
        var (svc, _) = TestDbHelper.Create(nameof(SavePresupuestoAsync_AsignaNumeroPresupuesto_Incremental));

        var p1 = await svc.SavePresupuestoAsync(new Presupuesto(), new List<PresupuestoDetalle>());
        var p2 = await svc.SavePresupuestoAsync(new Presupuesto(), new List<PresupuestoDetalle>());

        Assert.Equal(1, p1.NumeroPresupuesto);
        Assert.Equal(2, p2.NumeroPresupuesto);
    }

    [Fact]
    public async Task GetPresupuestoDetallesAsync_DevuelveSolo_LosDetallesDelPresupuestoPedido()
    {
        // Aislamiento: los detalles de un presupuesto no se mezclan con los de otro.
        var (svc, _) = TestDbHelper.Create(nameof(GetPresupuestoDetallesAsync_DevuelveSolo_LosDetallesDelPresupuestoPedido));
        var productoA = Guid.NewGuid();
        var productoB = Guid.NewGuid();

        var pres1 = await svc.SavePresupuestoAsync(new Presupuesto(), new List<PresupuestoDetalle>
        {
            new() { ProductoId = productoA, Quantity = 1, UnitPrice = 10m },
            new() { ProductoId = productoB, Quantity = 2, UnitPrice = 20m },
        });
        var pres2 = await svc.SavePresupuestoAsync(new Presupuesto(), new List<PresupuestoDetalle>
        {
            new() { ProductoId = productoA, Quantity = 5, UnitPrice = 99m },
        });

        var detalles1 = await svc.GetPresupuestoDetallesAsync(pres1.Id);
        var detalles2 = await svc.GetPresupuestoDetallesAsync(pres2.Id);

        Assert.Equal(2, detalles1.Count);
        Assert.All(detalles1, d => Assert.Equal(pres1.Id, d.PresupuestoId));
        Assert.Single(detalles2);
        Assert.Equal(5, detalles2[0].Quantity);
    }

    [Fact]
    public async Task SavePresupuestoAsync_NoSoportaActualizacion_ReSaveDelMismoPresupuesto_Falla()
    {
        // Comportamiento real: SavePresupuestoAsync SIEMPRE hace Add (insert).
        // Volver a guardar el mismo presupuesto intenta un segundo INSERT con la
        // misma PK y viola la restricción de clave primaria. No hay path de update
        // ni reemplazo de detalles.
        var (svc, _) = TestDbHelper.Create(nameof(SavePresupuestoAsync_NoSoportaActualizacion_ReSaveDelMismoPresupuesto_Falla));
        var presupuesto = new Presupuesto { Notas = "Original" };
        await svc.SavePresupuestoAsync(presupuesto, new List<PresupuestoDetalle>());

        presupuesto.Notas = "Modificado";

        await Assert.ThrowsAnyAsync<Exception>(
            () => svc.SavePresupuestoAsync(presupuesto, new List<PresupuestoDetalle>()));
    }

    [Fact]
    public async Task DeletePresupuestoAsync_SoftDeleteaCabecera_Y_HardDeleteaDetalles()
    {
        // Comportamiento real (asimétrico): la cabecera se marca IsDeleted = true
        // (soft delete, filtrada por el query filter global), pero los detalles se
        // eliminan físicamente con RemoveRange.
        var (svc, ctx) = TestDbHelper.Create(nameof(DeletePresupuestoAsync_SoftDeleteaCabecera_Y_HardDeleteaDetalles));
        var presupuesto = await svc.SavePresupuestoAsync(new Presupuesto(), new List<PresupuestoDetalle>
        {
            new() { ProductoId = Guid.NewGuid(), Quantity = 1, UnitPrice = 50m },
        });

        await svc.DeletePresupuestoAsync(presupuesto.Id);

        // La cabecera desaparece de las consultas normales...
        Assert.Empty(await svc.GetPresupuestosAsync());
        // ...pero sigue existiendo en la tabla, marcada como eliminada.
        var fila = await ctx.Presupuestos.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == presupuesto.Id);
        Assert.NotNull(fila);
        Assert.True(fila!.IsDeleted);

        // Los detalles se borran físicamente (no hay cascade de EF: lo hace el servicio a mano).
        Assert.Empty(await svc.GetPresupuestoDetallesAsync(presupuesto.Id));
    }

    [Fact]
    public async Task DeletePresupuestoAsync_ConIdInexistente_NoLanzaExcepcion()
    {
        var (svc, _) = TestDbHelper.Create(nameof(DeletePresupuestoAsync_ConIdInexistente_NoLanzaExcepcion));

        // No debe explotar si el id no existe.
        await svc.DeletePresupuestoAsync(Guid.NewGuid());

        Assert.Empty(await svc.GetPresupuestosAsync());
    }

    [Fact]
    public async Task GetPresupuestosAsync_Ordena_PorFecha_Descendente()
    {
        // El código real ordena por Date descendente (más reciente primero).
        // Date es DateTime (se persiste como TEXT ISO-8601, que ordena bien),
        // a diferencia de los montos decimal que NO se pueden ordenar server-side.
        var (svc, _) = TestDbHelper.Create(nameof(GetPresupuestosAsync_Ordena_PorFecha_Descendente));
        await svc.SavePresupuestoAsync(
            new Presupuesto { Date = new DateTime(2026, 1, 10), Notas = "Viejo" },
            new List<PresupuestoDetalle>());
        await svc.SavePresupuestoAsync(
            new Presupuesto { Date = new DateTime(2026, 3, 5), Notas = "Nuevo" },
            new List<PresupuestoDetalle>());
        await svc.SavePresupuestoAsync(
            new Presupuesto { Date = new DateTime(2026, 2, 1), Notas = "Medio" },
            new List<PresupuestoDetalle>());

        var result = await svc.GetPresupuestosAsync();

        Assert.Equal(new[] { "Nuevo", "Medio", "Viejo" }, result.Select(p => p.Notas).ToArray());
    }

    [Fact]
    public async Task SavePresupuestoAsync_PermiteCamposOpcionales_SinCliente_NiVencimiento()
    {
        // ClienteId y FechaVencimiento son nullables: un presupuesto puede existir
        // sin cliente asociado y sin fecha de vencimiento.
        var (svc, _) = TestDbHelper.Create(nameof(SavePresupuestoAsync_PermiteCamposOpcionales_SinCliente_NiVencimiento));
        var presupuesto = new Presupuesto
        {
            ClienteId = null,
            FechaVencimiento = null,
            Notas = string.Empty,
        };

        await svc.SavePresupuestoAsync(presupuesto, new List<PresupuestoDetalle>());

        var guardado = (await svc.GetPresupuestosAsync()).Single();
        Assert.Null(guardado.ClienteId);
        Assert.Null(guardado.FechaVencimiento);
        Assert.Equal(0m, guardado.Total); // sin detalles, total 0
    }

    [Fact]
    public async Task SavePresupuestoAsync_DespuesDeEliminarElUltimo_UsaElNumeroSiguiente_SinReutilizarElEliminado()
    {
        // Regresión: igual que en ventas, el presupuesto eliminado (soft-delete) conserva
        // su número en el índice UNIQUE; reutilizarlo hacía fallar todo presupuesto nuevo.
        var (svc, _) = TestDbHelper.Create(nameof(SavePresupuestoAsync_DespuesDeEliminarElUltimo_UsaElNumeroSiguiente_SinReutilizarElEliminado));
        var primero = await svc.SavePresupuestoAsync(new Presupuesto(), new List<PresupuestoDetalle>());
        await svc.DeletePresupuestoAsync(primero.Id);

        var segundo = await svc.SavePresupuestoAsync(new Presupuesto(), new List<PresupuestoDetalle>());

        Assert.Equal(2, segundo.NumeroPresupuesto);
    }
}
