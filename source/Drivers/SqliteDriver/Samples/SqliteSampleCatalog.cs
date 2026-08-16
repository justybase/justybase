namespace JustyBase.SqliteDriver.Samples;

public enum SqliteSampleObjectKind
{
    Table,
    View,
    Index,
    Trigger,
    BuiltInFunctionExample,
}

public sealed record SqliteSampleObjectDefinition(
    string Id,
    string DisplayName,
    SqliteSampleObjectKind Kind,
    string CreateSql,
    string? SeedSql,
    IReadOnlyList<string> Dependencies);

public sealed record SqliteSamplePack(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<SqliteSampleObjectDefinition> Objects);

public static class SqliteSampleCatalog
{
    public static IReadOnlyList<SqliteSamplePack> Packs { get; } =
    [
        CreateSalesPack(),
        CreateLibraryPack(),
    ];

    private static SqliteSamplePack CreateSalesPack()
    {
        const string customers = "sales.customers";
        const string products = "sales.products";
        const string orders = "sales.orders";
        const string orderItems = "sales.order_items";
        const string orderHistory = "sales.order_status_history";
        const string totals = "sales.customer_totals";

        return new SqliteSamplePack(
            "sales",
            "Sales",
            "Customers, products, orders, views, indexes and an audit trigger.",
            [
                new(
                    customers,
                    "customers",
                    SqliteSampleObjectKind.Table,
                    """
                    CREATE TABLE IF NOT EXISTS customers (
                        customer_id INTEGER PRIMARY KEY,
                        name TEXT NOT NULL,
                        email TEXT NOT NULL UNIQUE,
                        city TEXT NOT NULL,
                        created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
                    );
                    """,
                    """
                    INSERT OR IGNORE INTO customers (customer_id, name, email, city) VALUES
                        (1, 'Ada Lovelace', 'ada@example.test', 'London'),
                        (2, 'Grace Hopper', 'grace@example.test', 'New York'),
                        (3, 'Katherine Johnson', 'katherine@example.test', 'Washington');
                    """,
                    []),
                new(
                    products,
                    "products",
                    SqliteSampleObjectKind.Table,
                    """
                    CREATE TABLE IF NOT EXISTS products (
                        product_id INTEGER PRIMARY KEY,
                        name TEXT NOT NULL,
                        category TEXT NOT NULL,
                        price REAL NOT NULL CHECK (price >= 0),
                        stock INTEGER NOT NULL DEFAULT 0 CHECK (stock >= 0)
                    );
                    """,
                    """
                    INSERT OR IGNORE INTO products (product_id, name, category, price, stock) VALUES
                        (1, 'Mechanical keyboard', 'Hardware', 129.90, 24),
                        (2, 'Notebook', 'Stationery', 12.50, 100),
                        (3, 'USB-C dock', 'Hardware', 89.00, 15),
                        (4, 'Coffee mug', 'Office', 16.75, 48);
                    """,
                    []),
                new(
                    orders,
                    "orders",
                    SqliteSampleObjectKind.Table,
                    """
                    CREATE TABLE IF NOT EXISTS orders (
                        order_id INTEGER PRIMARY KEY,
                        customer_id INTEGER NOT NULL REFERENCES customers(customer_id),
                        ordered_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
                        status TEXT NOT NULL DEFAULT 'new' CHECK (status IN ('new', 'paid', 'shipped', 'cancelled'))
                    );
                    """,
                    """
                    INSERT OR IGNORE INTO orders (order_id, customer_id, ordered_at, status) VALUES
                        (1001, 1, '2026-01-12T09:15:00Z', 'paid'),
                        (1002, 1, '2026-02-03T14:20:00Z', 'shipped'),
                        (1003, 2, '2026-02-18T16:45:00Z', 'new'),
                        (1004, 3, '2026-03-01T11:10:00Z', 'paid');
                    """,
                    [customers]),
                new(
                    orderItems,
                    "order_items",
                    SqliteSampleObjectKind.Table,
                    """
                    CREATE TABLE IF NOT EXISTS order_items (
                        order_id INTEGER NOT NULL REFERENCES orders(order_id),
                        product_id INTEGER NOT NULL REFERENCES products(product_id),
                        quantity INTEGER NOT NULL CHECK (quantity > 0),
                        unit_price REAL NOT NULL CHECK (unit_price >= 0),
                        PRIMARY KEY (order_id, product_id)
                    );
                    """,
                    """
                    INSERT OR IGNORE INTO order_items (order_id, product_id, quantity, unit_price) VALUES
                        (1001, 1, 1, 129.90),
                        (1001, 2, 2, 12.50),
                        (1002, 3, 1, 89.00),
                        (1003, 4, 3, 16.75),
                        (1004, 1, 1, 129.90);
                    """,
                    [orders, products]),
                new(
                    orderHistory,
                    "order_status_history",
                    SqliteSampleObjectKind.Table,
                    """
                    CREATE TABLE IF NOT EXISTS order_status_history (
                        history_id INTEGER PRIMARY KEY,
                        order_id INTEGER NOT NULL REFERENCES orders(order_id),
                        old_status TEXT,
                        new_status TEXT NOT NULL,
                        changed_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
                    );
                    """,
                    null,
                    [orders]),
                new(
                    "sales.ix_orders_customer_date",
                    "orders by customer and date",
                    SqliteSampleObjectKind.Index,
                    "CREATE INDEX IF NOT EXISTS ix_orders_customer_date ON orders(customer_id, ordered_at);",
                    null,
                    [orders]),
                new(
                    "sales.ix_order_items_product",
                    "order items by product",
                    SqliteSampleObjectKind.Index,
                    "CREATE INDEX IF NOT EXISTS ix_order_items_product ON order_items(product_id);",
                    null,
                    [orderItems]),
                new(
                    totals,
                    "customer_totals",
                    SqliteSampleObjectKind.View,
                    """
                    CREATE VIEW IF NOT EXISTS customer_totals AS
                    SELECT
                        c.customer_id,
                        c.name,
                        COUNT(DISTINCT o.order_id) AS order_count,
                        ROUND(COALESCE(SUM(oi.quantity * oi.unit_price), 0), 2) AS total_spent,
                        MAX(o.ordered_at) AS last_order_at
                    FROM customers AS c
                    LEFT JOIN orders AS o ON o.customer_id = c.customer_id
                    LEFT JOIN order_items AS oi ON oi.order_id = o.order_id
                    GROUP BY c.customer_id, c.name;
                    """,
                    null,
                    [customers, orders, orderItems]),
                new(
                    "sales.function_examples",
                    "built-in function examples",
                    SqliteSampleObjectKind.BuiltInFunctionExample,
                    """
                    CREATE VIEW IF NOT EXISTS function_examples AS
                    SELECT
                        customer_id,
                        LOWER(name) AS normalized_name,
                        ROUND(total_spent, 2) AS rounded_total,
                        STRFTIME('%Y-%m', last_order_at) AS order_month,
                        COALESCE(order_count, 0) AS order_count
                    FROM customer_totals;
                    """,
                    null,
                    [totals]),
                new(
                    "sales.trg_order_status_history",
                    "order status audit trigger",
                    SqliteSampleObjectKind.Trigger,
                    """
                    CREATE TRIGGER IF NOT EXISTS trg_order_status_history
                    AFTER UPDATE OF status ON orders
                    WHEN OLD.status IS NOT NEW.status
                    BEGIN
                        INSERT INTO order_status_history (order_id, old_status, new_status)
                        VALUES (NEW.order_id, OLD.status, NEW.status);
                    END;
                    """,
                    null,
                    [orders, orderHistory]),
            ]);
    }

    private static SqliteSamplePack CreateLibraryPack()
    {
        const string authors = "library.authors";
        const string books = "library.books";
        const string members = "library.members";
        const string loans = "library.loans";
        const string loanHistory = "library.loan_history";
        const string available = "library.available_books";
        const string overdue = "library.overdue_loans";

        return new SqliteSamplePack(
            "library",
            "Library",
            "Authors, books, members, loan views, indexes and an audit trigger.",
            [
                new(
                    authors,
                    "authors",
                    SqliteSampleObjectKind.Table,
                    """
                    CREATE TABLE IF NOT EXISTS authors (
                        author_id INTEGER PRIMARY KEY,
                        name TEXT NOT NULL,
                        country TEXT
                    );
                    """,
                    """
                    INSERT OR IGNORE INTO authors (author_id, name, country) VALUES
                        (1, 'Mary Shelley', 'United Kingdom'),
                        (2, 'James Baldwin', 'United States'),
                        (3, 'Olga Tokarczuk', 'Poland');
                    """,
                    []),
                new(
                    books,
                    "books",
                    SqliteSampleObjectKind.Table,
                    """
                    CREATE TABLE IF NOT EXISTS books (
                        book_id INTEGER PRIMARY KEY,
                        author_id INTEGER NOT NULL REFERENCES authors(author_id),
                        title TEXT NOT NULL,
                        published_year INTEGER,
                        pages INTEGER CHECK (pages > 0)
                    );
                    """,
                    """
                    INSERT OR IGNORE INTO books (book_id, author_id, title, published_year, pages) VALUES
                        (1, 1, 'Frankenstein', 1818, 280),
                        (2, 2, 'Giovanni''s Room', 1956, 192),
                        (3, 3, 'Flights', 2007, 416),
                        (4, 1, 'The Last Man', 1826, 365);
                    """,
                    [authors]),
                new(
                    members,
                    "members",
                    SqliteSampleObjectKind.Table,
                    """
                    CREATE TABLE IF NOT EXISTS members (
                        member_id INTEGER PRIMARY KEY,
                        name TEXT NOT NULL,
                        email TEXT NOT NULL UNIQUE,
                        joined_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
                    );
                    """,
                    """
                    INSERT OR IGNORE INTO members (member_id, name, email, joined_at) VALUES
                        (1, 'Nina Simone', 'nina@example.test', '2026-01-05T10:00:00Z'),
                        (2, 'Alan Turing', 'alan@example.test', '2026-01-19T12:30:00Z'),
                        (3, 'Toni Morrison', 'toni@example.test', '2026-02-07T15:45:00Z');
                    """,
                    []),
                new(
                    loans,
                    "loans",
                    SqliteSampleObjectKind.Table,
                    """
                    CREATE TABLE IF NOT EXISTS loans (
                        loan_id INTEGER PRIMARY KEY,
                        book_id INTEGER NOT NULL REFERENCES books(book_id),
                        member_id INTEGER NOT NULL REFERENCES members(member_id),
                        borrowed_at TEXT NOT NULL,
                        due_at TEXT NOT NULL,
                        returned_at TEXT
                    );
                    """,
                    """
                    INSERT OR IGNORE INTO loans (loan_id, book_id, member_id, borrowed_at, due_at, returned_at) VALUES
                        (2001, 1, 1, '2026-02-01', '2026-02-15', '2026-02-12'),
                        (2002, 2, 2, '2026-02-20', '2026-03-06', NULL),
                        (2003, 3, 3, '2026-03-01', '2026-03-15', NULL);
                    """,
                    [books, members]),
                new(
                    loanHistory,
                    "loan_history",
                    SqliteSampleObjectKind.Table,
                    """
                    CREATE TABLE IF NOT EXISTS loan_history (
                        event_id INTEGER PRIMARY KEY,
                        loan_id INTEGER NOT NULL REFERENCES loans(loan_id),
                        event_name TEXT NOT NULL,
                        event_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
                    );
                    """,
                    null,
                    [loans]),
                new(
                    "library.ix_books_author_title",
                    "books by author and title",
                    SqliteSampleObjectKind.Index,
                    "CREATE INDEX IF NOT EXISTS ix_books_author_title ON books(author_id, title);",
                    null,
                    [books]),
                new(
                    "library.ix_loans_due_date",
                    "loans by due date",
                    SqliteSampleObjectKind.Index,
                    "CREATE INDEX IF NOT EXISTS ix_loans_due_date ON loans(due_at) WHERE returned_at IS NULL;",
                    null,
                    [loans]),
                new(
                    available,
                    "available_books",
                    SqliteSampleObjectKind.View,
                    """
                    CREATE VIEW IF NOT EXISTS available_books AS
                    SELECT b.book_id, b.title, a.name AS author
                    FROM books AS b
                    JOIN authors AS a ON a.author_id = b.author_id
                    WHERE NOT EXISTS (
                        SELECT 1 FROM loans AS l
                        WHERE l.book_id = b.book_id AND l.returned_at IS NULL
                    );
                    """,
                    null,
                    [authors, books, loans]),
                new(
                    overdue,
                    "overdue_loans",
                    SqliteSampleObjectKind.View,
                    """
                    CREATE VIEW IF NOT EXISTS overdue_loans AS
                    SELECT
                        l.loan_id,
                        b.title,
                        m.name AS member,
                        CAST(julianday('now') - julianday(l.due_at) AS INTEGER) AS days_overdue,
                        ROUND(julianday('now') - julianday(l.borrowed_at), 1) AS days_on_loan
                    FROM loans AS l
                    JOIN books AS b ON b.book_id = l.book_id
                    JOIN members AS m ON m.member_id = l.member_id
                    WHERE l.returned_at IS NULL AND julianday('now') > julianday(l.due_at);
                    """,
                    null,
                    [books, members, loans]),
                new(
                    "library.function_examples",
                    "built-in function examples",
                    SqliteSampleObjectKind.BuiltInFunctionExample,
                    """
                    CREATE VIEW IF NOT EXISTS function_examples AS
                    SELECT
                        book_id,
                        LOWER(title) AS normalized_title,
                        COALESCE(published_year, 0) AS published_year,
                        ROUND(pages / 100.0, 1) AS hundred_page_units
                    FROM books;
                    """,
                    null,
                    [books]),
                new(
                    "library.trg_loan_history",
                    "loan audit trigger",
                    SqliteSampleObjectKind.Trigger,
                    """
                    CREATE TRIGGER IF NOT EXISTS trg_loan_history
                    AFTER INSERT ON loans
                    BEGIN
                        INSERT INTO loan_history (loan_id, event_name)
                        VALUES (NEW.loan_id, 'borrowed');
                    END;
                    """,
                    null,
                    [loans, loanHistory]),
            ]);
    }
}
