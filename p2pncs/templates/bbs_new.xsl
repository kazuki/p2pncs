<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:import href="base.xsl" />
	<xsl:import href="validation.xsl" />
	<xsl:import href="mergeablefile_new.xsl" />

	<xsl:template name="_title">新規作成 :: BBS :: p2pncs</xsl:template>
	<xsl:template name="_css">
		<xsl:call-template name="create_validation_stylesheet" />
		<link type="text/css" rel="stylesheet" href="/css/bbs_new.css" />
	</xsl:template>
 
	<xsl:template match="/page">
		<h1>掲示板の新規作成</h1>
		<xsl:if test="$confirm_mode">
			<p>以下の内容で掲示板を作成してもよろしいですか？</p>
		</xsl:if>
		<xsl:if test="$success_mode">
			<p>以下の内容で掲示板を作成しました。</p>
			<p>
				<xsl:element name="a">
					<xsl:attribute name="href">
						<xsl:text>/</xsl:text>
						<xsl:value-of select="created/@key" />
					</xsl:attribute>
					<xsl:text>このリンク</xsl:text>
				</xsl:element>
				<xsl:text>をクリックすると作成した掲示板へ移動します。</xsl:text>
			</p>
		</xsl:if>
		<form method="post" action="/bbs/new">
			<div>
				<xsl:call-template name="check_validation" />
			</div>
			<table>
				<tr>
					<td class="header">タイトル: </td>
					<td>
						<xsl:call-template name="create_input_with_validation">
							<xsl:with-param name="size">70</xsl:with-param>
							<xsl:with-param name="name">title</xsl:with-param>
						</xsl:call-template>
					</td>
				</tr>
				<tr>
					<td class="header">認証サーバ: </td>
					<td>
						<xsl:call-template name="create_authserver_form" />
					</td>
				</tr>
				<tr>
					<td colspan="2"><hr /></td>
				</tr>
				<tr>
					<td colspan="2">
						<xsl:if test="not($confirm_mode or $success_mode)">
							<xsl:text>必要に応じて、最初の投稿をここで行うことが出来ます。</xsl:text>
						</xsl:if>
					</td>
				</tr>
				<tr>
					<td class="header">名前: </td>
					<td>
						<xsl:call-template name="create_input_with_validation">
							<xsl:with-param name="size">70</xsl:with-param>
							<xsl:with-param name="name">fpname</xsl:with-param>
						</xsl:call-template>
					</td>
				</tr>
				<tr>
					<td class="header">本文: </td>
					<td>
						<xsl:call-template name="create_textarea_with_validation">
							<xsl:with-param name="cols">70</xsl:with-param>
							<xsl:with-param name="rows">16</xsl:with-param>
							<xsl:with-param name="name">fpbody</xsl:with-param>
							<xsl:with-param name="class">bbs-body</xsl:with-param>
						</xsl:call-template>
					</td>
				</tr>
				<tr>
					<td colspan="2"><hr /></td>
				</tr>
				<tr>
					<td colspan="2" align="center">
						<xsl:if test="$confirm_mode">
							<input type="submit" value="再編集" name="re-edit" />
						</xsl:if>
						<xsl:if test="not($success_mode)">
							<input type="submit" value="作成" />
						</xsl:if>
					</td>
				</tr>
			</table>
		</form>
	</xsl:template>

</xsl:stylesheet>
