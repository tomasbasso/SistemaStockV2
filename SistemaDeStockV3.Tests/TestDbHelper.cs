using Microsoft.EntityFrameworkCore;
using SistemaDeStockV3.Data;
using SistemaDeStockV3.Models;
using SistemaDeStockV3.Services;

namespace SistemaDeStockV3.Tests;

/// <summary>
/// Helper para crear un DataService con base de datos SQLite en memoria para cada test.
/// Cada test obtiene su propia DB aislada via el nombre único del test.
/// </summary>
public static class TestDbHelper
{
    public static (DataService service, StockDbContext context) Create(string dbName)
    {
        var options = new DbContextOptionsBuilder<StockDbContext>()
            .UseSqlite($"DataSource=file:{dbName}?mode=memory&cache=shared")
            .Options;

        var context = new StockDbContext(options);
        context.Database.EnsureCreated();

        var service = new DataService(context);
        return (service, context);
    }
}
