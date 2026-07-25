-- =====================================================
-- VIGMA TIMBRADO - MIGRACIÓN: PORTAL CLIENTE
-- Creación de tabla CLIENTES + modificaciones para multi-tenant
-- =====================================================
-- Fecha: 2026-05-25
-- Descripción: Implementa arquitectura de distribuidor (cliente)
--              con múltiples tenants subordinados
-- =====================================================

USE vigma_timbrado;

-- =====================================================
-- 1. CREAR TABLA CLIENTES (NUEVA)
-- =====================================================
CREATE TABLE IF NOT EXISTS `clientes` (
  `id` BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  `nombre` VARCHAR(120) NOT NULL COMMENT 'Razón social del cliente/distribuidor',
  `rfc` VARCHAR(13) NULL COMMENT 'RFC del cliente (opcional)',
  `logo_path` VARCHAR(300) NULL COMMENT 'Ruta del logo para el portal cliente',
  `activo` BOOLEAN NOT NULL DEFAULT TRUE COMMENT 'Estado del cliente',
  `creado_utc` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Fecha creación UTC',

  -- Índices
  UNIQUE KEY `uk_rfc` (`rfc`),
  KEY `idx_activo` (`activo`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='Tabla de clientes/distribuidores que tienen tenants asociados';

-- =====================================================
-- 2. MODIFICAR TABLA TENANTS - AGREGAR cliente_id
-- =====================================================
ALTER TABLE `tenants`
ADD COLUMN `cliente_id` BIGINT NULL AFTER `activo`
COMMENT 'FK -> clientes. Un tenant pertenece a un cliente (distribuidor)';

-- Crear índice para búsquedas rápidas
ALTER TABLE `tenants`
ADD KEY `idx_tenants_cliente_id` (`cliente_id`);

-- Crear Foreign Key (nullable para no romper datos existentes)
ALTER TABLE `tenants`
ADD CONSTRAINT `fk_tenants_cliente_id`
  FOREIGN KEY (`cliente_id`)
  REFERENCES `clientes` (`id`)
  ON DELETE SET NULL
  ON UPDATE CASCADE;

-- =====================================================
-- 3. MODIFICAR TABLA USUARIOS_OFICINA - AGREGAR cliente_id
-- =====================================================
ALTER TABLE `usuarios_oficina`
ADD COLUMN `cliente_id` BIGINT NULL AFTER `activo`
COMMENT 'FK -> clientes. Usuario pertenece a un cliente';

-- Crear índice
ALTER TABLE `usuarios_oficina`
ADD KEY `idx_usuarios_oficina_cliente_id` (`cliente_id`);

-- Crear Foreign Key
ALTER TABLE `usuarios_oficina`
ADD CONSTRAINT `fk_usuarios_oficina_cliente_id`
  FOREIGN KEY (`cliente_id`)
  REFERENCES `clientes` (`id`)
  ON DELETE SET NULL
  ON UPDATE CASCADE;

-- =====================================================
-- 4. SEED DATA - CLIENTES DE PRUEBA
-- =====================================================
-- Insertar distribuidores/clientes de ejemplo
INSERT INTO `clientes` (`id`, `nombre`, `rfc`, `logo_path`, `activo`, `creado_utc`) VALUES
(1, 'Aceros del Norte SA', 'ACN123456ABC', '/logos/aceros-norte.png', TRUE, NOW()),
(2, 'Tech Solutions MX', 'TSM987654DEF', '/logos/tech-solutions.png', TRUE, NOW()),
(3, 'Distribuciones Monterrey', 'DIM456789GHI', '/logos/distrib-monterrey.png', TRUE, NOW());

-- =====================================================
-- 5. SEED DATA - TENANTS ASOCIADOS A CLIENTES
-- =====================================================
-- Los tenants de "Aceros del Norte" (cliente_id = 1)
UPDATE `tenants` SET `cliente_id` = 1 WHERE `id` IN (1, 2, 3, 4);

-- Los tenants de "Tech Solutions MX" (cliente_id = 2)
UPDATE `tenants` SET `cliente_id` = 2 WHERE `id` IN (5, 6, 7);

-- Los tenants de "Distribuciones Monterrey" (cliente_id = 3)
UPDATE `tenants` SET `cliente_id` = 3 WHERE `id` IN (8, 9, 10);

-- =====================================================
-- 6. SEED DATA - USUARIOS CLIENTE CON ROL "Cliente"
-- =====================================================
-- Usuario para "Aceros del Norte" (cliente_id = 1)
-- Password: Cliente123! (hash bcrypt de ejemplo)
INSERT INTO `usuarios_oficina` (
  `usuario`, `password_hash`, `rol`, `nombre`, `activo`, `cliente_id`
) VALUES (
  'aceros.norte@aceros.com',
  '$2y$10$W8vL6NhZ2k9pQ3rX4sM1Je5T2uJ6dF8hG3vK9bM4nL5oP6qR7sT8u', -- dummy hash
  'Cliente',
  'Administrador Aceros del Norte',
  TRUE,
  1
);

-- Usuario para "Tech Solutions MX" (cliente_id = 2)
INSERT INTO `usuarios_oficina` (
  `usuario`, `password_hash`, `rol`, `nombre`, `activo`, `cliente_id`
) VALUES (
  'tech.solutions@tech.com',
  '$2y$10$X9wM7OpA3k8pR4sY5tN2Kf6U3vJ7eG9iH4wL0cN5oM6pL7qS8tU9v',
  'Cliente',
  'Administrador Tech Solutions',
  TRUE,
  2
);

-- Usuario para "Distribuciones Monterrey" (cliente_id = 3)
INSERT INTO `usuarios_oficina` (
  `usuario`, `password_hash`, `rol`, `nombre`, `activo`, `cliente_id`
) VALUES (
  'distrib.monterrey@distrib.com',
  '$2y$10$Y0xN8PqB4l9qS5tZ6uO3Lg7V4wK8fH0jI5xM1dO6pN7qM8rT9uV0w',
  'Cliente',
  'Administrador Distribuciones Monterrey',
  TRUE,
  3
);

-- =====================================================
-- 7. VERIFICACIÓN - Mostrar estructura final
-- =====================================================
-- Descomenta para verificar después de ejecutar:
-- SELECT * FROM `clientes`;
-- SELECT `id`, `nombre`, `cliente_id`, `activo` FROM `tenants` ORDER BY `cliente_id`;
-- SELECT `usuario`, `rol`, `nombre`, `cliente_id` FROM `usuarios_oficina` WHERE `rol` = 'Cliente';

-- =====================================================
-- 8. NOTAS IMPORTANTES
-- =====================================================
/*
- Los campos cliente_id son NULLABLE para no romper datos existentes
- Los Foreign Keys tienen ON DELETE SET NULL (si se elimina cliente, no se borran tenants)
- Los datos de prueba incluyen:
  * 3 clientes/distribuidores
  * Tenants agrupados bajo cada cliente (4, 3, 3 tenants respectivamente)
  * Usuarios cliente con rol "Cliente" para acceder al portal

PRÓXIMOS PASOS:
1. Crear modelo Cliente.cs
2. Actualizar modelos Tenant.cs y UsuarioOficina.cs
3. Actualizar DbContext
4. Implementar seguridad en AccountController
5. Crear páginas del portal cliente
*/