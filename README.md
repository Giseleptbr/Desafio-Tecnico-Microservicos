# Desafio Técnico — Arquitetura de Microserviços com .NET e RabbitMQ

Este projeto foi desenvolvido como parte de um desafio técnico para demonstrar a construção de uma solução baseada em **microserviços**, comunicação assíncrona com **RabbitMQ** e separação clara de responsabilidades entre módulos.

---

## Visão Geral

A aplicação simula um cenário de **e-commerce simplificado**, dividido em dois domínios principais:

1. **Gestão de Estoque (Inventory API)**  
   Responsável pelo cadastro e controle de produtos, bem como pela atualização do estoque a partir de eventos de venda.

2. **Gestão de Vendas (Sales API)**  
   Responsável por registrar vendas e emitir eventos que notificam o serviço de estoque sobre movimentações.

Esses domínios comunicam-se de forma assíncrona por meio de **eventos publicados no RabbitMQ**, utilizando um modelo de **fanout**. A arquitetura também inclui um **API Gateway** para unificar o ponto de acesso dos consumidores externos.

---

## Funcionalidades

### **Inventory API**
- **Cadastro e manutenção de produtos**
  - Criar, atualizar, listar e excluir produtos.
  - Cada produto possui `SKU`, `Nome`, `Preço` e `Quantidade em Estoque`.
- **Validação de estoque**
  - Antes de efetuar baixa, verifica se há quantidade disponível para atender à venda.
- **Consumo de eventos**
  - Inscreve-se no tópico `ecommerce.sales` no RabbitMQ para receber notificações de vendas e atualizar o estoque automaticamente.
- **Autenticação via JWT**
  - Todas as operações protegidas exigem token válido.
- **Banco de dados**
  - Persistência em **SQLite** para simplificar execução local.

---

### **Sales API**
- **Registro de pedidos**
  - Recebe requisições de criação de pedidos, contendo uma lista de SKUs e quantidades.
- **Publicação de eventos**
  - Ao criar um pedido, publica um evento no tópico `ecommerce.sales` do RabbitMQ, informando os itens vendidos.
- **Integração com Inventory API**
  - Pode validar disponibilidade de estoque chamando a API de inventário antes da publicação do evento (dependendo da configuração).
- **Autenticação via JWT**
  - Protege endpoints sensíveis.

---

### **RabbitMQ**
- **Broker de mensagens**
  - Gerencia a comunicação assíncrona entre microserviços.
- **Exchange `ecommerce.sales`**
  - Tipo **fanout**, garantindo que todos os consumidores interessados recebam o evento de venda.
- **Mensagens**
  - Estruturadas em JSON, contendo informações do pedido e lista de itens vendidos.

---

### **API Gateway**
- **Unificação de acesso**
  - Redireciona requisições para os microserviços internos.
- **Isolamento**
  - Permite que clientes acessem apenas o gateway, sem conhecer diretamente os serviços.
- **Roteamento**
  - Define regras de mapeamento, por exemplo:
    - `/inventory/...` → Inventory API
    - `/sales/...` → Sales API

---

## Fluxo de Negócio

1. **Cadastro de produto**
   - Usuário (com JWT) cadastra produto na Inventory API.
2. **Venda**
   - Usuário cria pedido via Sales API.
3. **Publicação de evento**
   - Sales API envia mensagem para o RabbitMQ (`ecommerce.sales`).
4. **Processamento no estoque**
   - Inventory API consome evento, atualiza o estoque e salva alteração no banco.
5. **Consulta**
   - Usuário pode verificar o estoque atualizado na Inventory API.

---

## Diferenciais Implementados
- Separação real de responsabilidades (cada serviço com seu banco).
- Comunicação híbrida:
  - **Síncrona** (HTTP) para integrações diretas.
  - **Assíncrona** (RabbitMQ) para propagação de eventos.
- Segurança com JWT.
- Arquitetura flexível para troca de banco de dados (SQL Server ou SQLite).

---

## Possíveis Extensões
- Persistência de pedidos na Sales API.
- Implementação de relatórios de vendas.
- Monitoração e métricas (Prometheus/Grafana).
- Orquestração de workflows (Saga Pattern).

## 📸 Capturas de Tela — Desenvolvimento e Funcionamento

A seguir, estão as principais capturas de tela geradas durante o desenvolvimento e execução do projeto, ilustrando o fluxo de operação dos microserviços, integração via RabbitMQ e consumo das APIs.

---

### 1️⃣ Publicação e Consumo de Mensagens via RabbitMQ
![RabbitMQ Connections](./capturas%20de%20tela%20-%20desenvolvimento/Captura%20de%20Tela%202025-08-12%20%C3%A0s%2018.00.23.png)

**Descrição:**  
Tela do painel de administração do RabbitMQ mostrando as conexões ativas dos microserviços **Inventory.Api** e **Sales.Api**.  
Cada conexão representa um serviço conectado ao broker para publicar ou consumir eventos.

---

### 2️⃣ Painel Geral do RabbitMQ
![RabbitMQ Overview](./capturas%20de%20tela%20-%20desenvolvimento/Captura%20de%20Tela%202025-08-12%20%C3%A0s%2018.01.09.png)

**Descrição:**  
Visão geral do RabbitMQ com métricas em tempo real sobre:
- **Taxa de publicação** e **consumo** de mensagens.
- Número de conexões, canais, exchanges e filas existentes.
- Status de recursos como memória, CPU e disco.

---

### 3️⃣ Execução do Fluxo de Pedidos e Estoque
![Execução APIs](./capturas%20de%20tela%20-%20desenvolvimento/Captura%20de%20Tela%202025-08-14%20%C3%A0s%2010.50.43.png)

**Descrição:**  
Exemplo prático do uso das APIs:
1. Criação de produto no **Inventory.Api** via `POST /api/products`.  
2. Criação de pedido no **Sales.Api** via `POST /api/orders`, que dispara evento no RabbitMQ.
3. Consulta do produto mostrando atualização do estoque.

---

### 4️⃣ Docker e Build dos Serviços
![Docker Build](./capturas%20de%20tela%20-%20desenvolvimento/Captura%20de%20Tela%202025-08-14%20%C3%A0s%2012.55.08.png)

**Descrição:**  
Compilação e inicialização de todos os serviços via `docker compose up`, incluindo:
- **Inventory.Api**
- **Sales.Api**
- **ApiGateway**
- **RabbitMQ**
- Bancos SQL de cada serviço

---

### 5️⃣ Painel RabbitMQ — Modo Detalhado
![RabbitMQ Detalhes](./capturas%20de%20tela%20-%20desenvolvimento/RabbitMQ%20.png)

**Descrição:**  
Visualização detalhada das **exchanges**, **filas** e **consumidores** no RabbitMQ, confirmando que os eventos estão sendo roteados corretamente entre os microserviços.

---

