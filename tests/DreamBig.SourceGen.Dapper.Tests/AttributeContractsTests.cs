using System;
using System.Data;
using DreamBig.SourceGen.Dapper.Attributes;
using Shouldly;
using Xunit;

namespace DreamBig.SourceGen.Dapper.Tests;

public sealed class AttributeContractsTests
{
    [Fact]
    public void DbUnitOfWorkAttribute_ShouldBeCreatable()
    {
        var attribute = new DbUnitOfWorkAttribute();
        attribute.ShouldNotBeNull();
    }

    [Fact]
    public void DbTableAttribute_ShouldSetDefaults()
    {
        var attribute = new DbTableAttribute("Customers");

        attribute.TableName.ShouldBe("Customers");
        attribute.Schema.ShouldBeNull();
        attribute.PrimaryKey.ShouldBeNull();
    }

    [Fact]
    public void DbTableAttribute_ShouldAllowPrimaryKeyDefinition()
    {
        var attribute = new DbTableAttribute("Customers")
        {
            PrimaryKey = "Id",
        };

        attribute.PrimaryKey.ShouldBe("Id");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void DbTableAttribute_ShouldValidateTableName(string tableName)
    {
        Should.Throw<ArgumentException>(() => new DbTableAttribute(tableName));
    }

    [Fact]
    public void DbColumnAttribute_ShouldSetColumnName()
    {
        var attribute = new DbColumnAttribute("full_name");
        attribute.ColumnName.ShouldBe("full_name");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void DbColumnAttribute_ShouldValidateColumnName(string columnName)
    {
        Should.Throw<ArgumentException>(() => new DbColumnAttribute(columnName));
    }

    [Fact]
    public void DbStoredProcedureAttribute_ShouldSetDefaults()
    {
        var attribute = new DbStoredProcedureAttribute("usp_customer_summary");

        attribute.Name.ShouldBe("usp_customer_summary");
        attribute.Schema.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void DbStoredProcedureAttribute_ShouldValidateName(string name)
    {
        Should.Throw<ArgumentException>(() => new DbStoredProcedureAttribute(name));
    }

    [Fact]
    public void DbParamAttribute_ShouldSetDefaultsAndAllowMetadata()
    {
        var attribute = new DbParamAttribute("@customerId")
        {
            Direction = DbParamDirection.InputOutput,
            DbType = DbType.Int32,
            Size = 8,
        };

        attribute.Name.ShouldBe("@customerId");
        attribute.Direction.ShouldBe(DbParamDirection.InputOutput);
        attribute.DbType.ShouldBe(DbType.Int32);
        attribute.Size.ShouldBe(8);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void DbParamAttribute_ShouldValidateName(string name)
    {
        Should.Throw<ArgumentException>(() => new DbParamAttribute(name));
    }

    [Fact]
    public void DbJoinAttribute_ShouldSetValues()
    {
        var attribute = new DbJoinAttribute
        {
            JoinType = JoinType.Left,
            JoinTableA = typeof(string),
            JoinTableB = typeof(int),
            JoinColumnA = "Id",
            JoinColumnB = "CustomerId",
            AliasA = "customers",
            AliasB = "orders",
            Where = "customers.Id = @customerId",
            On = "orders.IsActive = 1",
            OrderBy = "customers.Id",
            OrderByDirection = OrderByDirection.Desc,
        };

        attribute.JoinType.ShouldBe(JoinType.Left);
        attribute.JoinTableA.ShouldBe(typeof(string));
        attribute.JoinTableB.ShouldBe(typeof(int));
        attribute.JoinColumnA.ShouldBe("Id");
        attribute.JoinColumnB.ShouldBe("CustomerId");
        attribute.AliasA.ShouldBe("customers");
        attribute.AliasB.ShouldBe("orders");
        attribute.Where.ShouldBe("customers.Id = @customerId");
        attribute.On.ShouldBe("orders.IsActive = 1");
        attribute.OrderBy.ShouldBe("customers.Id");
        attribute.OrderByDirection.ShouldBe(OrderByDirection.Desc);
    }
}
