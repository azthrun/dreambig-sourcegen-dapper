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
        attribute.Schema.ShouldBe("dbo");
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
        attribute.Schema.ShouldBe("dbo");
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
        var attribute = new DbJoinAttribute(JoinType.Left, "[dbo].[Orders] o", "c.Id", "o.CustomerId")
        {
            Alias = "o",
        };

        attribute.JoinType.ShouldBe(JoinType.Left);
        attribute.Table.ShouldBe("[dbo].[Orders] o");
        attribute.Left.ShouldBe("c.Id");
        attribute.Right.ShouldBe("o.CustomerId");
        attribute.Alias.ShouldBe("o");
    }

    [Theory]
    [InlineData("", "c.Id", "o.CustomerId")]
    [InlineData("[dbo].[Orders] o", "", "o.CustomerId")]
    [InlineData("[dbo].[Orders] o", "c.Id", "")]
    public void DbJoinAttribute_ShouldValidateConstructorArguments(string table, string left, string right)
    {
        Should.Throw<ArgumentException>(() => new DbJoinAttribute(JoinType.Inner, table, left, right));
    }
}
