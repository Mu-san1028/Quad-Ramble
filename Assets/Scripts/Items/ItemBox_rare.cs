/// <summary>RoundManagerのレア抽選を使う箱。取得・復活・同期は通常箱と共通。</summary>
public class ItemBox_rare : ItemBox
{
    protected override Items[] GetItemTable(RoundManager round) => round.RareItems;
}
