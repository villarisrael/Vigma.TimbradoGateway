-- ════════════════════════════════════════════════════════════════════════════════════
-- MIGRATION: Crear tabla Clientes y relaciones multi-tenant
-- ════════════════════════════════════════════════════════════════════════════════════

-- 1. Crear tabla clientes
CREATE TABLE IF NOT EXISTS clientes (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    nombre VARCHAR(120) NOT NULL,
    rfc VARCHAR(13) NULL,
    logo_path VARCHAR(255) NULL,
    activo TINYINT(1) NOT NULL DEFAULT 1,
    creado_utc DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY uk_rfc (rfc)
);

-- 2. Agregar columna cliente_id a tenants (si no existe)
ALTER TABLE tenants ADD COLUMN cliente_id BIGINT NULL AFTER id;
ALTER TABLE tenants ADD CONSTRAINT fk_tenants_clientes FOREIGN KEY (cliente_id) REFERENCES clientes(id) ON DELETE SET NULL;

-- 3. Agregar columna cliente_id a usuarios_oficina (si no existe)
ALTER TABLE usuarios_oficina ADD COLUMN cliente_id BIGINT NULL AFTER id;
ALTER TABLE usuarios_oficina ADD CONSTRAINT fk_usuarios_clientes FOREIGN KEY (cliente_id) REFERENCES clientes(id) ON DELETE CASCADE;

-- 4. Seed data: Insertar 3 clientes de prueba
INSERT INTO clientes (nombre, rfc, logo_path, activo, creado_utc) VALUES
('Cliente Demo 1', 'DEMO100000000', NULL, 1, NOW()),
('Cliente Demo 2', 'DEMO200000000', NULL, 1, NOW()),
('Cliente Demo 3', 'DEMO300000000', NULL, 1, NOW());

-- 5. Asignar algunos tenants a los clientes
UPDATE tenants SET cliente_id = 1 WHERE id IN (1, 2, 3);
UPDATE tenants SET cliente_id = 2 WHERE id IN (4, 5, 6);
UPDATE tenants SET cliente_id = 3 WHERE id IN (7, 8);

-- 6. Crear usuario cliente de prueba para Cliente Demo 1
-- Contraseña: "Test123!" hasheada con BCrypt
INSERT INTO usuarios_oficina (usuario, password_hash, rol, nombre, activo, creado_utc, cliente_id)
VALUES ('cea@demo.com', '$2a$11$3hztWHiKNwLvLDMb4dDw4OpRoXvxG0m8NycYYQP0E9NKjHpGMgIhK', 'Cliente', 'Usuario CEA Demo', 1, NOW(), 1);
