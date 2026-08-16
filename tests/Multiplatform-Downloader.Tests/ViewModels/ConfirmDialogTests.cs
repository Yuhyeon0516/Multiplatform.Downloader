using Multiplatform_Downloader.ViewModels;

namespace Multiplatform_Downloader.Tests.ViewModels;

/// <summary>삭제 확인 대화상자(테마 일치 커스텀 창) 동작 검증.</summary>
public class ConfirmDialogTests
{
    [Fact]
    public async Task should_set_confirmed_true_when_confirm_clicked()
    {
        var vm = new ConfirmDialogViewModel("선택 삭제", "3개 항목을 삭제할까요?");

        await vm.Confirm();

        Assert.True(vm.Confirmed);
    }

    [Fact]
    public async Task should_keep_confirmed_false_when_canceled()
    {
        var vm = new ConfirmDialogViewModel("선택 삭제", "3개 항목을 삭제할까요?");

        await vm.Cancel();

        Assert.False(vm.Confirmed);
        Assert.Equal("선택 삭제", vm.Title);
        Assert.Equal("삭제", vm.ConfirmText); // 기본 확인 라벨
    }
}
