<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:import href="base.xsl" />

	<xsl:template name="_title">Statistics :: p2pncs</xsl:template>
	<xsl:template name="_css">
		<link type="text/css" rel="stylesheet" href="/css/statistics.css" />
	</xsl:template>
 
	<xsl:template match="/page">
		<h1>統計情報</h1>
		<p>
			<xsl:text>起動時間: </xsl:text>
			<xsl:variable name="sec" select="statistics/@running-time" />
			<xsl:if test="$sec &gt; 60">
				<xsl:if test="$sec &gt; 3600">
					<xsl:if test="$sec &gt; 86400">
						<xsl:value-of select="floor($sec div 86400)" />
						<xsl:text>日</xsl:text>
					</xsl:if>
					<xsl:value-of select="floor($sec div 3600) mod 24" />
					<xsl:text>時間</xsl:text>
				</xsl:if>
				<xsl:value-of select="floor($sec div 60) mod 60" />
				<xsl:text>分</xsl:text>
			</xsl:if>
			<xsl:value-of select="$sec mod 60" />
			<xsl:text>秒</xsl:text>
		</p>
		<xsl:apply-templates select="statistics/*" />
	</xsl:template>

	<xsl:template match="traffic">
		<h2>トラフィック</h2>
		<table>
			<thead>
				<tr>
					<td />
					<td>平均</td>
					<td>合計</td>
				</tr>
			</thead>
			<tbody>
				<tr>
					<th>受信バイト数: </th>
					<td><xsl:value-of select="floor(average/@recv-bytes * 8 div 100) div 10" />kbps</td>
					<td><xsl:value-of select="total/@recv-bytes" /></td>
				</tr>
				<tr>
					<th>受信パケット数: </th>
					<td><xsl:value-of select="floor(average/@recv-packets)" />pps</td>
					<td><xsl:value-of select="total/@recv-packets" /></td>
				</tr>
				<tr>
					<th>送信バイト数: </th>
					<td><xsl:value-of select="floor(average/@send-bytes * 8 div 100) div 10" />kbps</td>
					<td><xsl:value-of select="total/@send-bytes" /></td>
				</tr>
				<tr>
					<th>送信パケット数: </th>
					<td><xsl:value-of select="floor(average/@send-packets)" />pps</td>
					<td><xsl:value-of select="total/@send-packets" /></td>
				</tr>
			</tbody>
		</table>
	</xsl:template>

	<xsl:template match="messaging">
		<h2>メッセージング</h2>
		<table>
			<thead>
				<tr>
					<td rowspan="2" />
					<td colspan="2">RTT</td>
					<td colspan="4">問い合わせ</td>
				</tr>
				<tr>
					<td>平均</td>
					<td>分散</td>
					<td>成功数</td>
					<td>失敗数</td>
					<td>成功率</td>
					<td>再送数</td>
				</tr>
			</thead>
			<tbody>
				<xsl:for-each select="entry">
					<tr>
						<td><xsl:value-of select="position()" /></td>
						<td><xsl:value-of select="floor(@rtt-avg)" />ms</td>
						<td>±<xsl:value-of select="floor(@rtt-sd)" />ms</td>
						<td><xsl:value-of select="@success" /></td>
						<td><xsl:value-of select="@fail" /></td>
						<td><xsl:value-of select="floor(@success * 100 div (@success + @fail))" /> %</td>
						<td><xsl:value-of select="@retries" /></td>
					</tr>
				</xsl:for-each>
			</tbody>
		</table>
	</xsl:template>

	<xsl:template match="kbr">
		<h2>Key-based Router</h2>
		<table>
			<thead>
				<tr>
					<td colspan="3">問い合わせ</td>
					<td colspan="2">ホップ数</td>
					<td colspan="2">RTT</td>
				</tr>
				<tr>
					<td>成功数</td>
					<td>失敗数</td>
					<td>成功率</td>
					<td>平均</td>
					<td>分散</td>
					<td>平均</td>
					<td>分散</td>
				</tr>
			</thead>
			<tbody>
				<tr>
					<td><xsl:value-of select="@success" /></td>
					<td><xsl:value-of select="@fail" /></td>
					<td><xsl:value-of select="floor(@success * 100 div (@success + @fail))" /> %</td>
					<td><xsl:value-of select="floor(@hops-avg)" /></td>
					<td>±<xsl:value-of select="floor(@hops-sd)" /></td>
					<td><xsl:value-of select="floor(@rtt-avg)" /> msec</td>
					<td>±<xsl:value-of select="floor(@rtt-sd)" /> msec</td>
				</tr>
			</tbody>
		</table>
	</xsl:template>

	<xsl:template match="mcr">
		<h2>多重暗号経路</h2>
		<table>
			<thead>
				<tr>
					<td rowspan="2">累計構築数</td>
					<td rowspan="2">成功率</td>
					<td colspan="2">生存時間</td>
				</tr>
				<tr>
					<td>平均</td>
					<td>分散</td>
				</tr>
			</thead>
			<tbody>
				<tr>
					<td><xsl:value-of select="@success" /></td>
					<td><xsl:value-of select="floor(@success * 100 div (@success + @fail))"/> %</td>
					<td><xsl:value-of select="floor(@lifetime-avg)" /> sec</td>
					<td>±<xsl:value-of select="floor(@lifetime-sd)" /> sec</td>
				</tr>
			</tbody>
		</table>
	</xsl:template>

	<xsl:template match="ac">
		<h2>匿名コネクション</h2>
		<table>
			<thead>
				<tr>
					<td>確立数</td>
					<td>確立失敗数</td>
					<td>確立成功率</td>
				</tr>
			</thead>
			<tbody>
				<tr>
					<td><xsl:value-of select="@success" /></td>
					<td><xsl:value-of select="@fail" /></td>
					<td><xsl:value-of select="floor(@success * 100 div (@success + @fail))"/> %</td>
				</tr>
			</tbody>
		</table>
	</xsl:template>

	<xsl:template match="ac">
		<h2>マージ及び管理可能な分散ファイルシステム</h2>
		<table>
			<thead>
				<tr>
					<td colspan="3">マージ</td>
				</tr>
				<tr>
					<td>成功数</td>
					<td>失敗数</td>
					<td>成功率</td>
				</tr>
			</thead>
			<tbody>
				<tr>
					<td><xsl:value-of select="@success" /></td>
					<td><xsl:value-of select="@fail" /></td>
					<td><xsl:value-of select="floor(@success * 100 div (@success + @fail))"/> %</td>
				</tr>
			</tbody>
		</table>
	</xsl:template>
</xsl:stylesheet>
