using JustyBase.Common.Tools.ImportHelpers;
using JustyBase.ImportExport.Import;
using DatabaseTypeChooser = JustyBase.Common.Tools.ImportHelpers.DatabaseTypeChooser;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.ViewModels.Documents;

namespace JustyBase.Tests;

public sealed class ImportTypeSelectionTests
{
    [Fact]
    public void ColumnInGrid_ChangingType_WritesToTargetArray()
    {
        var target = new DbTypeWithSize[]
        {
            new(DbSimpleType.Numeric) { NumericPrecision = 10, NumericScale = 2 }
        };
        var grid = new ColumnInGrid("price", "NUMERIC(10,2)", target, 0);

        grid.SelectedChoice = TypeChoice.All.First(c => c.Value == DbSimpleType.Nvarchar);

        Assert.Equal(DbSimpleType.Nvarchar, target[0].DatabaseTypeSimple);
        Assert.Equal(DatabaseTypeChooser.DEFAULT_NVARCHAR_LENGTH, target[0].TextLength);
        Assert.Equal("NVARCHAR (text)", grid.SelectedChoice.Label);
        Assert.Equal("price", grid.ColumnName);
    }

    [Fact]
    public void ColumnInGrid_Resetting_RestoresOriginalType()
    {
        var target = new DbTypeWithSize[]
        {
            new(DbSimpleType.Date)
        };
        var grid = new ColumnInGrid("d", "DATE", target, 0);

        grid.SelectedChoice = TypeChoice.All.First(c => c.Value == DbSimpleType.Integer);
        Assert.Equal(DbSimpleType.Integer, target[0].DatabaseTypeSimple);

        grid.ResetToDetectedCommand.Execute(null);

        Assert.Equal(DbSimpleType.Date, target[0].DatabaseTypeSimple);
        Assert.Equal(DbSimpleType.Date, grid.SelectedChoice.Value);
    }

    [Fact]
    public void ColumnInGrid_ForcedNumeric_KeepsDetectedSizes()
    {
        var target = new DbTypeWithSize[]
        {
            new(DbSimpleType.Numeric) { NumericPrecision = 14, NumericScale = 3 }
        };
        var grid = new ColumnInGrid("v", "NUMERIC(14,3)", target, 0);

        grid.SelectedChoice = TypeChoice.All.First(c => c.Value == DbSimpleType.Numeric);

        Assert.Equal(14, target[0].NumericPrecision);
        Assert.Equal(3, target[0].NumericScale);
    }

    [Fact]
    public void ColumnInGrid_Constructor_DoesNotTriggerTypeChanged()
    {
        var target = new DbTypeWithSize[]
        {
            new(DbSimpleType.Integer)
        };
        int callbackCount = 0;

        // Regression: constructing the row used to fire typeChanged (via the initial
        // SelectedChoice assignment), which cancelled the sheet validation and left the
        // Import Start buttons disabled forever.
        var grid = new ColumnInGrid("c", target[0], target, 0, 0, t => t.ToString(), () => callbackCount++);

        Assert.Equal(0, callbackCount);

        grid.SelectedChoice = TypeChoice.All.First(c => c.Value == DbSimpleType.Nvarchar);

        Assert.Equal(1, callbackCount);
        Assert.Equal(DbSimpleType.Nvarchar, target[0].DatabaseTypeSimple);
    }

    [Fact]
    public void ColumnInGrid_SameChoice_DoesNotReFireTypeChanged()
    {
        var target = new DbTypeWithSize[]
        {
            new(DbSimpleType.Date)
        };
        int callbackCount = 0;
        var grid = new ColumnInGrid("d", target[0], target, 0, 0, t => t.ToString(), () => callbackCount++);

        // Re-selecting the already-applied type must not be reported as a change.
        grid.SelectedChoice = TypeChoice.All.First(c => c.Value == DbSimpleType.Date);
        Assert.Equal(0, callbackCount);

        grid.SelectedChoice = TypeChoice.All.First(c => c.Value == DbSimpleType.Integer);
        Assert.Equal(1, callbackCount);
    }

    [Fact]
    public void ColumnInGrid_Constructor_RejectsOutOfRangeIndex()
    {
        var target = new DbTypeWithSize[]
        {
            new(DbSimpleType.Integer)
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new ColumnInGrid("c", "BIGINT", target, 1));
    }

    [Fact]
    public void ColumnInGrid_Override_UpdatesDetectedType()
    {
        var target = new DbTypeWithSize[]
        {
            new(DbSimpleType.Numeric) { NumericPrecision = 10, NumericScale = 2 }
        };
        var grid = new ColumnInGrid("price", "NUMERIC(10,2)", target, 0);

        grid.SelectedChoice = TypeChoice.All.First(c => c.Value == DbSimpleType.Nvarchar);

        Assert.Equal($"NVARCHAR({DatabaseTypeChooser.DEFAULT_NVARCHAR_LENGTH})", grid.DetectedType);
    }

    [Fact]
    public void ColumnInGrid_ResetToDetected_RestoresDetectedTypeLabel()
    {
        var target = new DbTypeWithSize[]
        {
            new(DbSimpleType.Date)
        };
        var grid = new ColumnInGrid("d", "DATE", target, 0, t => t.ToString());

        grid.SelectedChoice = TypeChoice.All.First(c => c.Value == DbSimpleType.Integer);
        Assert.Equal("BIGINT", grid.DetectedType);

        grid.ResetToDetectedCommand.Execute(null);

        Assert.Equal("DATE", grid.DetectedType);
    }

    [Fact]
    public void ColumnInGrid_DetectedType_UsesProvidedFormatter()
    {
        var target = new DbTypeWithSize[]
        {
            new(DbSimpleType.Numeric) { NumericPrecision = 8, NumericScale = 2 }
        };
        var grid = new ColumnInGrid("v", "NUMBER (8,2)", target, 0, t => t.ToString(DatabaseTypeEnum.Oracle));

        grid.SelectedChoice = TypeChoice.All.First(c => c.Value == DbSimpleType.Integer);

        Assert.Equal("INTEGER", grid.DetectedType);
    }

    [Fact]
    public void ColumnInGrid_SeparatesDetectedAndSelectedTypes()
    {
        var target = new DbTypeWithSize[]
        {
            new(DbSimpleType.Numeric) { NumericPrecision = 10, NumericScale = 2 }
        };
        var grid = new ColumnInGrid("price", target[0], target, 0, detectedTextLength: 400, typeFormatter: t => t.ToString());

        grid.SelectedChoice = TypeChoice.All.First(c => c.Value == DbSimpleType.Nvarchar);

        Assert.Equal("NUMERIC(10,2)", grid.DetectedType);
        Assert.Equal("NVARCHAR(400)", grid.SelectedType);
        Assert.True(grid.IsOverridden);

        grid.ResetToDetectedCommand.Execute(null);

        Assert.Equal("NUMERIC(10,2)", grid.DetectedType);
        Assert.Equal("NUMERIC(10,2)", grid.SelectedType);
        Assert.False(grid.IsOverridden);
    }

    [Fact]
    public async Task ImportFromExcelFile_ConcurrentDetection_IsSerializedAndSafe()
    {
        string csv = Path.Combine(Path.GetTempPath(), $"jbt_{Guid.NewGuid():N}.csv");
        try
        {
            await File.WriteAllTextAsync(csv, "id,price\n1,10.5\n2,20.75\n3,1\n");

            var import = new ImportFromExcelFile(null, null)
            {
                FilePath = csv
            };
            Assert.True(import.InitImport());
            string sheet = import.SheetNamesToImport![0];

            var tasks = new[]
            {
                import.DetectSheetAsync(sheet),
                import.DetectSheetAsync(sheet),
                import.DetectSheetAsync(sheet)
            };
            var results = await Task.WhenAll(tasks);

            Assert.All(results, c => Assert.NotNull(c));
            Assert.Same(results[0], results[1]);
            Assert.Same(results[0], results[2]);
        }
        finally
        {
            try
            {
                File.Delete(csv);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task ImportFromExcelFile_DetectThenImport_ReusesCachedTypes()
    {
        string csv = Path.Combine(Path.GetTempPath(), $"jbt_{Guid.NewGuid():N}.csv");
        try
        {
            await File.WriteAllTextAsync(csv, "id,price\n1,10.5\n2,20.75\n3,1\n");

            var import = new ImportFromExcelFile(null, null)
            {
                FilePath = csv
            };
            Assert.True(import.InitImport());
            string sheet = import.SheetNamesToImport![0];

            var chooser = await import.DetectSheetAsync(sheet);
            Assert.NotNull(chooser);
            Assert.True(chooser.RowsCount >= 2);
            DbSimpleType originalSecondColumn = chooser.ColumnTypesBestMatch![1].DatabaseTypeSimple;

            chooser.ColumnTypesBestMatch[0] = new DbTypeWithSize(DbSimpleType.Nvarchar)
            {
                TextLength = DatabaseTypeChooser.DEFAULT_NVARCHAR_LENGTH
            };

            DbImportJob? job = null;
            int rows = 0;
            await foreach (var j in import.ReadFileAndReturnSingleImportJobs())
            {
                job = j;
                while (job.AsReader.Read())
                {
                    rows++;
                }
            }

            Assert.NotNull(job);
            Assert.Equal(3, rows);
            Assert.Same(chooser.ColumnTypesBestMatch, job.ColumnTypesBestMatch);
            Assert.Equal(DbSimpleType.Nvarchar, job.ColumnTypesBestMatch[0].DatabaseTypeSimple);
            Assert.Equal(originalSecondColumn, job.ColumnTypesBestMatch[1].DatabaseTypeSimple);
        }
        finally
        {
            try
            {
                File.Delete(csv);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task ImportFromExcelFile_InvalidateTypeCache_ForcesRescan()
    {
        string csv = Path.Combine(Path.GetTempPath(), $"jbt_{Guid.NewGuid():N}.csv");
        try
        {
            await File.WriteAllTextAsync(csv, "id\n1\n2\n3\n");

            var import = new ImportFromExcelFile(null, null)
            {
                FilePath = csv
            };
            Assert.True(import.InitImport());
            string sheet = import.SheetNamesToImport![0];

            var first = await import.DetectSheetAsync(sheet);
            Assert.NotNull(first);
            Assert.Same(first, await import.DetectSheetAsync(sheet));

            import.InvalidateTypeCache();
            var second = await import.DetectSheetAsync(sheet);
            Assert.NotNull(second);
            Assert.NotSame(first, second);
        }
        finally
        {
            try
            {
                File.Delete(csv);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task ImportFromExcelFile_Validation_ReportsInvalidOverrideBeforeImport()
    {
        string csv = Path.Combine(Path.GetTempPath(), $"jbt_invalid_{Guid.NewGuid():N}.csv");
        try
        {
            await File.WriteAllTextAsync(csv, "code\n1\nnot-an-integer\n\n");
            var import = new ImportFromExcelFile(null, null) { FilePath = csv };
            Assert.True(import.InitImport());
            string sheet = import.SheetNamesToImport![0];
            DatabaseTypeChooser chooser = (await import.DetectSheetAsync(sheet))!;
            chooser.ColumnTypesBestMatch![0] = new DbTypeWithSize(DbSimpleType.Integer);

            IReadOnlyList<ImportValidationError> errors = await import.ValidateSelectedSheetsAsync();

            ImportValidationError error = Assert.Single(errors);
            Assert.Equal(sheet, error.SheetName);
            Assert.Equal(3, error.RowNumber);
            Assert.Equal("CODE", error.ColumnName);
            Assert.Equal(ImportColumnKind.Integer, error.SelectedKind);
            Assert.Equal("not-an-integer", error.Value);
            Assert.Same(chooser, import.GetTypeChooser(sheet));
            Assert.Single(chooser.ValidationErrors);
        }
        finally
        {
            try
            {
                File.Delete(csv);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task ImportFromExcelFile_Validation_AllowsEmptyCellsForSelectedTypes()
    {
        string csv = Path.Combine(Path.GetTempPath(), $"jbt_nulls_{Guid.NewGuid():N}.csv");
        try
        {
            await File.WriteAllTextAsync(csv, "value\n\n");
            var import = new ImportFromExcelFile(null, null) { FilePath = csv };
            Assert.True(import.InitImport());
            string sheet = import.SheetNamesToImport![0];
            DatabaseTypeChooser chooser = (await import.DetectSheetAsync(sheet))!;
            chooser.ColumnTypesBestMatch![0] = new DbTypeWithSize(DbSimpleType.Date);

            IReadOnlyList<ImportValidationError> errors = await import.ValidateSelectedSheetsAsync();

            Assert.Empty(errors);
            Assert.Empty(chooser.ValidationErrors);

            int rows = 0;
            await foreach (DbImportJob job in import.ReadFileAndReturnSingleImportJobs())
            {
                while (job.AsReader.Read())
                    rows++;
            }
            Assert.Equal(1, rows);
        }
        finally
        {
            try
            {
                File.Delete(csv);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task ImportFromExcelFile_TypeInference_MatchesSharedGoldenMatrix()
    {
        // CSV rows drive the shared vscode chooser via the host's raw-token CSV path,
        // so host output must match the shared golden matrix (2.2-B). Host ToString for a
        // DATETIME maps to "TIMESTAMP".
        string csv = Path.Combine(Path.GetTempPath(), $"jbt_golden_{Guid.NewGuid():N}.csv");
        try
        {
            await File.WriteAllTextAsync(csv, "num,intcol,dec,code,dateiso,datedotted,mix\n1,100,1.5,001,2024-01-15,07.06.2024,abc\n2,200,2.25,002,2024-02-01,08.06.2024,x\n");

            var import = new ImportFromExcelFile(null, null)
            {
                FilePath = csv
            };
            Assert.True(import.InitImport());
            string sheet = import.SheetNamesToImport![0];

            DatabaseTypeChooser chooser = (await import.DetectSheetAsync(sheet))!;
            string[] actual = chooser.ColumnTypesBestMatch!.Select(t => t.ToString(DatabaseTypeEnum.NetezzaSQL)).ToArray();

            Assert.Equal(
            [
                "BIGINT",       // num       1,2
                "BIGINT",       // intcol    100,200
                "NUMERIC(16,2)", // dec      1.5,2.25
                "NVARCHAR(20)", // code      001,002 leading zeros stay text
                "DATE",         // dateiso   2024-01-15
                "TIMESTAMP",    // datedotted 07.06.2024 → DATETIME
                "NVARCHAR(20)"  // mix       abc,x
            ], actual);
        }
        finally
        {
            try
            {
                File.Delete(csv);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task ImportFromExcelFile_AllColumnsAsText_ForcesNvarchar()
    {
        string csv = Path.Combine(Path.GetTempPath(), $"jbt_text_{Guid.NewGuid():N}.csv");
        try
        {
            await File.WriteAllTextAsync(csv, "id,amount,when\n1,10.5,2024-01-15\n2,20.75,2024-02-01\n");

            var import = new ImportFromExcelFile(null, null)
            {
                FilePath = csv,
                TreatAllColumnsAsText = true
            };
            Assert.True(import.InitImport());
            string sheet = import.SheetNamesToImport![0];

            DatabaseTypeChooser chooser = (await import.DetectSheetAsync(sheet))!;

            Assert.All(chooser.ColumnTypesBestMatch!, t => Assert.Equal(DbSimpleType.Nvarchar, t.DatabaseTypeSimple));
        }
        finally
        {
            try
            {
                File.Delete(csv);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task ImportFromExcelFile_TextForcedColumnName_PeselStaysNvarchar()
    {
        string csv = Path.Combine(Path.GetTempPath(), $"jbt_pesel_{Guid.NewGuid():N}.csv");
        try
        {
            await File.WriteAllTextAsync(csv, "pesel,amount\n85122312345,10.5\n92010112345,20.75\n");

            var import = new ImportFromExcelFile(null, null)
            {
                FilePath = csv
            };
            Assert.True(import.InitImport());
            string sheet = import.SheetNamesToImport![0];

            DatabaseTypeChooser chooser = (await import.DetectSheetAsync(sheet))!;

            Assert.Equal(DbSimpleType.Nvarchar, chooser.ColumnTypesBestMatch![0].DatabaseTypeSimple);
            Assert.Equal(DbSimpleType.Numeric, chooser.ColumnTypesBestMatch[1].DatabaseTypeSimple);
        }
        finally
        {
            try
            {
                File.Delete(csv);
            }
            catch (IOException)
            {
            }
        }
    }
}
