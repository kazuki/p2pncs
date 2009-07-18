$(function() {
	$("#postButton").click(function() {
		$.create_post_dialog ("/wiki/" + $("#postKey").val() + "/", {
			"name": $("#postName").val(),
			"body": $("#postBody").val(),
			"title": $("#postPage").val(),
			"auth": $("#authsvr").val()
		}, function () {
			window.location = "/wiki/" + $("#postKey").val() + "/" + encodeURIComponent ($("#postPage").val());
		}, function () {});
		return false;
	});
});
