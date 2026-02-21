using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Models;

namespace Vigma.TimbradoGateway.Infrastructure.Repositories
{
    public interface IRepoUsuariosOficina
    {
        Task<UsuarioOficina?> GetByIdAsync(long id, CancellationToken ct = default);
        Task<UsuarioOficina?> GetByUsuarioAsync(string usuario, CancellationToken ct = default);

        Task<IReadOnlyList<UsuarioOficina>> ListAsync(
            string? q = null,
            bool? activo = null,
            int skip = 0,
            int take = 50,
            CancellationToken ct = default);

        Task<long> CountAsync(string? q = null, bool? activo = null, CancellationToken ct = default);

        Task<UsuarioOficina> CreateAsync(UsuarioOficina u, CancellationToken ct = default);
        Task<bool> UpdateAsync(UsuarioOficina u, CancellationToken ct = default);

        Task<bool> SetActivoAsync(long id, bool activo, CancellationToken ct = default);
        Task<bool> DeleteAsync(long id, CancellationToken ct = default);

        Task<bool> UsuarioExisteAsync(string usuario, long? ignorarId = null, CancellationToken ct = default);
    }

    public class RepoUsuariosOficina : IRepoUsuariosOficina
    {
        private readonly TimbradoDbContext _db;

        public RepoUsuariosOficina(TimbradoDbContext db)
        {
            _db = db;
        }

        private DbSet<UsuarioOficina> Set => _db.UsuariosOficina;

        public Task<UsuarioOficina?> GetByIdAsync(long id, CancellationToken ct = default)
            => Set.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

        public Task<UsuarioOficina?> GetByUsuarioAsync(string usuario, CancellationToken ct = default)
        {
            usuario = (usuario ?? "").Trim();
            return Set.AsNoTracking().FirstOrDefaultAsync(x => x.Usuario == usuario, ct);
        }

        public async Task<IReadOnlyList<UsuarioOficina>> ListAsync(
            string? q = null,
            bool? activo = null,
            int skip = 0,
            int take = 50,
            CancellationToken ct = default)
        {
            if (skip < 0) skip = 0;
            if (take <= 0) take = 50;
            if (take > 500) take = 500;

            IQueryable<UsuarioOficina> query = Set.AsNoTracking();

            q = (q ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(x =>
                    x.Usuario.Contains(q) ||
                    (x.Nombre != null && x.Nombre.Contains(q)) ||
                    x.Rol.Contains(q));
            }

            if (activo.HasValue)
                query = query.Where(x => x.Activo == activo.Value);

            return await query
                .OrderByDescending(x => x.Activo)
                .ThenBy(x => x.Usuario)
                .Skip(skip)
                .Take(take)
                .ToListAsync(ct);
        }

        public async Task<long> CountAsync(string? q = null, bool? activo = null, CancellationToken ct = default)
        {
            IQueryable<UsuarioOficina> query = Set.AsNoTracking();

            q = (q ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(x =>
                    x.Usuario.Contains(q) ||
                    (x.Nombre != null && x.Nombre.Contains(q)) ||
                    x.Rol.Contains(q));
            }

            if (activo.HasValue)
                query = query.Where(x => x.Activo == activo.Value);

            return await query.LongCountAsync(ct);
        }

        public async Task<UsuarioOficina> CreateAsync(UsuarioOficina u, CancellationToken ct = default)
        {
            if (u == null) throw new ArgumentNullException(nameof(u));

            u.Usuario = (u.Usuario ?? "").Trim();
            u.Rol = string.IsNullOrWhiteSpace(u.Rol) ? "Oficina" : u.Rol.Trim();

            if (string.IsNullOrWhiteSpace(u.Usuario))
                throw new InvalidOperationException("El campo Usuario es requerido.");

            if (string.IsNullOrWhiteSpace(u.PasswordHash))
                throw new InvalidOperationException("El campo PasswordHash es requerido.");

            if (await UsuarioExisteAsync(u.Usuario, ignorarId: null, ct))
                throw new InvalidOperationException("Ese usuario ya existe.");

            if (u.Creado == default)
                u.Creado = DateTime.UtcNow;

            try
            {
                await Set.AddAsync(u, ct);
                await _db.SaveChangesAsync(ct);
                return u;
            }
            catch (DbUpdateException ex)
            {
                throw new InvalidOperationException("No se pudo crear el usuario (error de base de datos).", ex);
            }
        }

        public async Task<bool> UpdateAsync(UsuarioOficina u, CancellationToken ct = default)
        {
            if (u == null) throw new ArgumentNullException(nameof(u));
            if (u.Id <= 0) throw new InvalidOperationException("Id inválido.");

            u.Usuario = (u.Usuario ?? "").Trim();
            u.Rol = string.IsNullOrWhiteSpace(u.Rol) ? "Oficina" : u.Rol.Trim();

            if (string.IsNullOrWhiteSpace(u.Usuario))
                throw new InvalidOperationException("El campo Usuario es requerido.");

            if (await UsuarioExisteAsync(u.Usuario, ignorarId: u.Id, ct))
                throw new InvalidOperationException("Ya existe otro usuario con ese nombre.");

            try
            {
                Set.Update(u);
                return (await _db.SaveChangesAsync(ct)) > 0;
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }
            catch (DbUpdateException ex)
            {
                throw new InvalidOperationException("No se pudo actualizar el usuario (error de base de datos).", ex);
            }
        }

        public async Task<bool> SetActivoAsync(long id, bool activo, CancellationToken ct = default)
        {
            if (id <= 0) return false;

            var row = await Set.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row == null) return false;

            row.Activo = activo;

            try
            {
                await _db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException ex)
            {
                throw new InvalidOperationException("No se pudo cambiar el estado del usuario.", ex);
            }
        }

        public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
        {
            if (id <= 0) return false;

            var row = await Set.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row == null) return false;

            try
            {
                Set.Remove(row);
                await _db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException ex)
            {
                throw new InvalidOperationException("No se pudo eliminar el usuario (error de base de datos).", ex);
            }
        }

        public Task<bool> UsuarioExisteAsync(string usuario, long? ignorarId = null, CancellationToken ct = default)
        {
            usuario = (usuario ?? "").Trim();

            IQueryable<UsuarioOficina> query = Set.AsNoTracking().Where(x => x.Usuario == usuario);

            if (ignorarId.HasValue)
                query = query.Where(x => x.Id != ignorarId.Value);

            return query.AnyAsync(ct);
        }
    }
}
